using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

internal sealed class WasapiAudioCaptureSession : IAudioCaptureSession
{
    private const int PacketCapacity = 512;
    private const int FileBufferSize = 64 * 1024;

    private readonly AudioCaptureOptions _options;
    private readonly WasapiCapture _capture;
    private readonly MMDevice _device;
    private readonly ICaptureJournalStore _journals;
    private readonly Channel<AudioPacket> _packets;
    private readonly TaskCompletionSource<Exception?> _captureStopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly object _failureLock = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly WaveFormat _format;
    private readonly AudioSampleEncoding _encoding;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly List<string> _chunkPaths = [];
    private Task? _writerTask;
    private Exception? _failure;
    private AudioCaptureResult? _result;
    private long _totalBytes;
    private int _nextChunkIndex;
    private int _faultRaised;
    private bool _stopRequested;
    private bool _disposed;

    internal WasapiAudioCaptureSession(
        AudioCaptureOptions options,
        WasapiCapture capture,
        MMDevice device,
        ICaptureJournalStore journals)
    {
        _options = options;
        _capture = capture;
        _device = device;
        _journals = journals;
        _deviceId = device.ID;
        _deviceName = device.FriendlyName;
        _format = NormalizeFormat(capture.WaveFormat);
        _encoding = GetEncoding(_format);
        _packets = Channel.CreateBounded<AudioPacket>(
            new BoundedChannelOptions(PacketCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
    }

    public Guid SessionId => _options.SessionId;

    public Guid RecordingId => _options.RecordingId;

    public bool IsRecording { get; private set; }

    public event EventHandler<AudioCaptureProgress>? ProgressChanged;

    public event EventHandler<AudioCaptureChunkCompletedEventArgs>? ChunkCompleted;

    public event EventHandler<AudioCaptureFaultedEventArgs>? CaptureFaulted;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(_options.SessionDirectory);

        await SaveJournalAsync(CaptureJournalState.Capturing, cancellationToken)
            .ConfigureAwait(false);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _writerTask = Task.Run(WritePacketsAsync, CancellationToken.None);

        try
        {
            _capture.StartRecording();
            IsRecording = true;
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or COMException)
        {
            SetFailure(error);
            _packets.Writer.TryComplete(error);
            await SaveJournalAsync(CaptureJournalState.Interrupted, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AudioCaptureResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _stopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_result is not null)
            {
                return _result;
            }

            _stopRequested = true;
            await SaveJournalAsync(CaptureJournalState.Stopping, cancellationToken)
                .ConfigureAwait(false);

            if (IsRecording)
            {
                _capture.StopRecording();
            }
            else
            {
                _packets.Writer.TryComplete();
            }

            Exception? captureError = await _captureStopped.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (_writerTask is not null)
            {
                await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Exception? failure = GetFailure() ?? captureError;
            if (failure is not null)
            {
                await SaveJournalAsync(CaptureJournalState.Interrupted, CancellationToken.None)
                    .ConfigureAwait(false);
                throw new IOException("Microphone capture stopped before it could be finalized.", failure);
            }

            DateTimeOffset endedAt = DateTimeOffset.Now;
            _result = new AudioCaptureResult(
                SessionId,
                RecordingId,
                _startedAt,
                endedAt,
                _deviceId,
                _deviceName,
                _format.SampleRate,
                _format.BitsPerSample,
                _format.Channels,
                _encoding,
                _chunkPaths.ToArray(),
                Interlocked.Read(ref _totalBytes));

            await SaveJournalAsync(CaptureJournalState.Finalized, CancellationToken.None)
                .ConfigureAwait(false);
            return _result;
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (IsRecording)
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The journal and chunks intentionally remain available for recovery.
            }
        }

        _disposed = true;
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        try
        {
            _capture.Dispose();
        }
        finally
        {
            try
            {
                _device.Dispose();
            }
            catch (Exception error) when (
                error is COMException
                    or InvalidCastException
                    or InvalidComObjectException)
            {
                // NAudio may already have released the underlying endpoint COM object.
            }
        }

        _stopGate.Dispose();
    }

    private static WaveFormat NormalizeFormat(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : format;
    }

    private static AudioSampleEncoding GetEncoding(WaveFormat format)
    {
        return format.Encoding switch
        {
            WaveFormatEncoding.Pcm => AudioSampleEncoding.Pcm,
            WaveFormatEncoding.IeeeFloat => AudioSampleEncoding.IeeeFloat,
            _ => throw new NotSupportedException(
                $"The microphone format {format.Encoding} is not supported."),
        };
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0 || _captureStopped.Task.IsCompleted)
        {
            return;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(eventArgs.BytesRecorded);
        Buffer.BlockCopy(eventArgs.Buffer, 0, buffer, 0, eventArgs.BytesRecorded);
        AudioPacket packet = new(buffer, eventArgs.BytesRecorded);

        if (!_packets.Writer.TryWrite(packet))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            SetFailure(new IOException(
                "The audio writer could not keep up with microphone capture."));
            QueueCaptureStop();
            return;
        }

        long totalBytes = Interlocked.Add(ref _totalBytes, eventArgs.BytesRecorded);
        float peak = AudioLevelMeter.GetPeak(
            eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded),
            _format.BitsPerSample,
            _encoding);
        ProgressChanged?.Invoke(
            this,
            new AudioCaptureProgress(
                TimeSpan.FromSeconds(totalBytes / (double)_format.AverageBytesPerSecond),
                peak,
                totalBytes));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        IsRecording = false;
        if (eventArgs.Exception is not null)
        {
            SetFailure(eventArgs.Exception);
        }
        else if (!_stopRequested)
        {
            SetFailure(new IOException("The microphone device stopped unexpectedly."));
        }

        _packets.Writer.TryComplete();
        _captureStopped.TrySetResult(eventArgs.Exception);
    }

    private async Task WritePacketsAsync()
    {
        FileStream? chunk = null;
        long chunkBytes = 0;
        long targetBytes = GetChunkByteCount();
        int chunkSequence = -1;

        try
        {
            await foreach (AudioPacket packet in _packets.Reader.ReadAllAsync())
            {
                try
                {
                    int offset = 0;
                    while (offset < packet.Count)
                    {
                        if (chunk is null)
                        {
                            (chunk, chunkSequence) = OpenNextChunk();
                        }

                        int bytesToWrite = (int)Math.Min(
                            targetBytes - chunkBytes,
                            packet.Count - offset);
                        await chunk.WriteAsync(
                                packet.Buffer.AsMemory(offset, bytesToWrite))
                            .ConfigureAwait(false);
                        offset += bytesToWrite;
                        chunkBytes += bytesToWrite;

                        if (chunkBytes >= targetBytes)
                        {
                            await FinalizeChunkAsync(
                                    chunk,
                                    chunkSequence,
                                    chunkBytes)
                                .ConfigureAwait(false);
                            chunk = null;
                            chunkBytes = 0;
                            chunkSequence = -1;
                            await SaveJournalAsync(
                                    CaptureJournalState.Capturing,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(packet.Buffer);
                }
            }

            if (chunk is not null)
            {
                await FinalizeChunkAsync(
                        chunk,
                        chunkSequence,
                        chunkBytes)
                    .ConfigureAwait(false);
                chunk = null;
            }
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            SetFailure(error);
            QueueCaptureStop();
            throw;
        }
        finally
        {
            if (chunk is not null)
            {
                await chunk.DisposeAsync().ConfigureAwait(false);
            }

            while (_packets.Reader.TryRead(out AudioPacket? abandoned))
            {
                if (abandoned is not null)
                {
                    ArrayPool<byte>.Shared.Return(abandoned.Buffer);
                }
            }
        }
    }

    private (FileStream Stream, int Sequence) OpenNextChunk()
    {
        int sequence = _nextChunkIndex;
        string path = Path.Combine(
            _options.SessionDirectory,
            $"{sequence:D6}.pcm");
        FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        _chunkPaths.Add(path);
        _nextChunkIndex++;
        return (stream, sequence);
    }

    private async Task FinalizeChunkAsync(
        FileStream chunk,
        int sequence,
        long byteLength)
    {
        string path = chunk.Name;
        await chunk.FlushAsync().ConfigureAwait(false);
        chunk.Flush(flushToDisk: true);
        await chunk.DisposeAsync().ConfigureAwait(false);

        TimeSpan start = TimeSpan.FromSeconds(
            sequence * _options.ChunkDuration.TotalSeconds);
        TimeSpan duration = TimeSpan.FromSeconds(
            byteLength / (double)_format.AverageBytesPerSecond);
        ChunkCompleted?.Invoke(
            this,
            new AudioCaptureChunkCompletedEventArgs(
                new AudioCaptureChunk(
                    RecordingId,
                    sequence,
                    path,
                    start,
                    duration,
                    _format.SampleRate,
                    _format.BitsPerSample,
                    _format.Channels,
                    _encoding,
                    byteLength)));
    }

    private long GetChunkByteCount()
    {
        long requested = checked(
            (long)(_format.AverageBytesPerSecond * _options.ChunkDuration.TotalSeconds));
        long aligned = requested - (requested % _format.BlockAlign);
        return Math.Max(_format.BlockAlign, aligned);
    }

    private Task SaveJournalAsync(
        CaptureJournalState state,
        CancellationToken cancellationToken)
    {
        CaptureJournal journal = new(
            SessionId,
            RecordingId,
            _options.Kind,
            state,
            _startedAt,
            DateTimeOffset.Now,
            _deviceId,
            _format.SampleRate,
            _format.BitsPerSample,
            _format.Channels,
            _encoding,
            _nextChunkIndex,
            Interlocked.Read(ref _totalBytes));
        return _journals.SaveAsync(journal, cancellationToken);
    }

    private void SetFailure(Exception error)
    {
        bool first;
        lock (_failureLock)
        {
            first = _failure is null;
            _failure ??= error;
        }

        if (first && Interlocked.Exchange(ref _faultRaised, 1) == 0)
        {
            CaptureFaulted?.Invoke(this, new AudioCaptureFaultedEventArgs(error));
        }
    }

    private Exception? GetFailure()
    {
        lock (_failureLock)
        {
            return _failure;
        }
    }

    private void QueueCaptureStop()
    {
        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                if (state is not WasapiAudioCaptureSession session)
                {
                    return;
                }

                try
                {
                    if (session.IsRecording)
                    {
                        session._capture.StopRecording();
                    }
                }
                catch (InvalidOperationException)
                {
                    session._packets.Writer.TryComplete();
                }
            },
            this);
    }

    private sealed record AudioPacket(byte[] Buffer, int Count);
}
