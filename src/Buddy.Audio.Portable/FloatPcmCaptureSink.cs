using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Audio.Portable;

/// <summary>
/// Durable, bounded writer shared by native desktop microphone backends.
/// Native callbacks stay non-blocking while chunk files and the recovery
/// journal are flushed on a single background writer.
/// </summary>
public sealed class FloatPcmCaptureSink : IAsyncDisposable
{
    private const int PacketCapacity = 512;
    private const int FileBufferSize = 64 * 1024;
    private const int BitsPerSample = 32;

    private readonly AudioCaptureOptions _options;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ICaptureJournalStore _journals;
    private readonly Channel<AudioPacket> _packets;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly object _failureLock = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly List<string> _chunkPaths = [];
    private Task? _writerTask;
    private AudioCaptureResult? _result;
    private Exception? _failure;
    private long _totalBytes;
    private int _nextChunkIndex;
    private int _faultRaised;
    private bool _started;
    private bool _stopRequested;
    private bool _disposed;

    public FloatPcmCaptureSink(
        AudioCaptureOptions options,
        string deviceId,
        string deviceName,
        int sampleRate,
        int channels,
        ICaptureJournalStore journals)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (sampleRate is < 8_000 or > 192_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        _deviceId = deviceId;
        _deviceName = deviceName;
        _sampleRate = sampleRate;
        _channels = channels;
        _journals = journals ?? throw new ArgumentNullException(nameof(journals));
        _packets = Channel.CreateBounded<AudioPacket>(
            new BoundedChannelOptions(PacketCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
    }

    public event EventHandler<AudioCaptureProgress>? ProgressChanged;

    public event EventHandler<AudioCaptureChunkCompletedEventArgs>? ChunkCompleted;

    public event EventHandler<AudioCaptureFaultedEventArgs>? CaptureFaulted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The PCM sink has already started.");
        }

        Directory.CreateDirectory(_options.SessionDirectory);
        await SaveJournalAsync(CaptureJournalState.Capturing, cancellationToken)
            .ConfigureAwait(false);
        _started = true;
        _writerTask = Task.Run(WritePacketsAsync, CancellationToken.None);
    }

    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        if (!_started || _stopRequested || samples.IsEmpty)
        {
            return false;
        }

        int byteCount = checked(samples.Length * sizeof(float));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        MemoryMarshal.AsBytes(samples).CopyTo(buffer);
        if (!_packets.Writer.TryWrite(new AudioPacket(buffer, byteCount)))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            SetFailure(new IOException(
                "The audio writer could not keep up with microphone capture."));
            _stopRequested = true;
            _packets.Writer.TryComplete();
            return false;
        }

        float peak = 0;
        foreach (float sample in samples)
        {
            float value = Math.Abs(sample);
            if (float.IsFinite(value))
            {
                peak = Math.Max(peak, Math.Min(1, value));
            }
        }

        long totalBytes = Interlocked.Add(ref _totalBytes, byteCount);
        ProgressChanged?.Invoke(
            this,
            new AudioCaptureProgress(
                TimeSpan.FromSeconds(totalBytes / (double)AverageBytesPerSecond),
                peak,
                totalBytes));
        return true;
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

            if (!_started)
            {
                throw new InvalidOperationException("The PCM sink has not started.");
            }

            _stopRequested = true;
            await SaveJournalAsync(CaptureJournalState.Stopping, cancellationToken)
                .ConfigureAwait(false);
            _packets.Writer.TryComplete();
            if (_writerTask is not null)
            {
                await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Exception? failure = GetFailure();
            if (failure is not null)
            {
                await SaveJournalAsync(
                        CaptureJournalState.Interrupted,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw new IOException(
                    "Microphone capture stopped before it could be finalized.",
                    failure);
            }

            _result = new AudioCaptureResult(
                _options.SessionId,
                _options.RecordingId,
                _startedAt,
                DateTimeOffset.Now,
                _deviceId,
                _deviceName,
                _sampleRate,
                BitsPerSample,
                _channels,
                AudioSampleEncoding.IeeeFloat,
                _chunkPaths.ToArray(),
                Interlocked.Read(ref _totalBytes));
            await SaveJournalAsync(
                    CaptureJournalState.Finalized,
                    CancellationToken.None)
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

        if (_started && !_stopRequested)
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Raw chunks and the journal remain available for recovery.
            }
        }

        _disposed = true;
        _stopGate.Dispose();
    }

    private int AverageBytesPerSecond => checked(
        _sampleRate * _channels * (BitsPerSample / 8));

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
                await FinalizeChunkAsync(chunk, chunkSequence, chunkBytes)
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
        string path = Path.Combine(_options.SessionDirectory, $"{sequence:D6}.pcm");
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
        ChunkCompleted?.Invoke(
            this,
            new AudioCaptureChunkCompletedEventArgs(
                new AudioCaptureChunk(
                    _options.RecordingId,
                    sequence,
                    path,
                    TimeSpan.FromSeconds(
                        sequence * _options.ChunkDuration.TotalSeconds),
                    TimeSpan.FromSeconds(
                        byteLength / (double)AverageBytesPerSecond),
                    _sampleRate,
                    BitsPerSample,
                    _channels,
                    AudioSampleEncoding.IeeeFloat,
                    byteLength)));
    }

    private long GetChunkByteCount()
    {
        int blockAlign = checked(_channels * (BitsPerSample / 8));
        long requested = checked(
            (long)(AverageBytesPerSecond * _options.ChunkDuration.TotalSeconds));
        long aligned = requested - (requested % blockAlign);
        return Math.Max(blockAlign, aligned);
    }

    private Task SaveJournalAsync(
        CaptureJournalState state,
        CancellationToken cancellationToken)
    {
        CaptureJournal journal = new(
            _options.SessionId,
            _options.RecordingId,
            _options.Kind,
            state,
            _startedAt,
            DateTimeOffset.Now,
            _deviceId,
            _sampleRate,
            BitsPerSample,
            _channels,
            AudioSampleEncoding.IeeeFloat,
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

    private sealed record AudioPacket(byte[] Buffer, int Count);
}
