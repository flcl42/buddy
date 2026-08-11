using Buddy.Core.Abstractions;
using MiniAudioEx.Core.StandardAPI;
using MiniAudioEx.Native;

namespace Buddy.Audio.Portable;

internal sealed class MiniAudioCaptureSession : AudioRecorder, IAudioCaptureSession
{
    private readonly AudioCaptureOptions _options;
    private readonly AudioDevice _device;
    private readonly ICaptureJournalStore _journals;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private FloatPcmCaptureSink? _sink;
    private AudioCaptureResult? _result;
    private bool _started;
    private bool _stopRequested;
    private bool _disposed;

    internal MiniAudioCaptureSession(
        AudioCaptureOptions options,
        AudioDevice device,
        ICaptureJournalStore journals)
        : base(
            checked((uint)options.PreferredSampleRate),
            checked((uint)options.PreferredChannels))
    {
        _options = options;
        _device = device;
        _journals = journals;
        SetDevice(device);
    }

    public Guid SessionId => _options.SessionId;

    public Guid RecordingId => _options.RecordingId;

    public bool IsRecording => _started && !_stopRequested && IsActive;

    public event EventHandler<AudioCaptureProgress>? ProgressChanged;

    public event EventHandler<AudioCaptureChunkCompletedEventArgs>? ChunkCompleted;

    public event EventHandler<AudioCaptureFaultedEventArgs>? CaptureFaulted;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException(
                "The microphone capture session has already started.");
        }

        FloatPcmCaptureSink sink = new(
            _options,
            MiniAudioCaptureService.GetId(_device),
            _device.Name,
            checked((int)sampleRate),
            checked((int)channels),
            _journals);
        sink.ProgressChanged += OnProgressChanged;
        sink.ChunkCompleted += OnChunkCompleted;
        sink.CaptureFaulted += OnSinkFaulted;
        _sink = sink;
        await sink.StartAsync(cancellationToken).ConfigureAwait(false);

        if (!Initialize() || !Start())
        {
            throw new InvalidOperationException(
                $"Could not start microphone capture on {_device.Name}.");
        }

        _started = true;
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

            FloatPcmCaptureSink sink = _sink
                ?? throw new InvalidOperationException(
                    "Microphone capture is not active.");
            _stopRequested = true;
            if (IsActive)
            {
                Stop();
            }

            _result = await sink
                .StopAsync(cancellationToken)
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

        if (_sink is not null && !_stopRequested)
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (
                error is IOException or InvalidOperationException)
            {
                // The durable journal and completed chunks remain recoverable.
            }
        }

        _disposed = true;
        if (_sink is not null)
        {
            _sink.ProgressChanged -= OnProgressChanged;
            _sink.ChunkCompleted -= OnChunkCompleted;
            _sink.CaptureFaulted -= OnSinkFaulted;
            await _sink.DisposeAsync().ConfigureAwait(false);
            _sink = null;
        }

        base.Dispose();
        _stopGate.Dispose();
    }

    protected override bool OnStart() => true;

    protected override unsafe void OnProcess(
        NativeArray<float> data,
        uint frameCount)
    {
        if (_stopRequested || data.IsEmpty)
        {
            return;
        }

        ReadOnlySpan<float> samples = new(
            data.Pointer.ToPointer(),
            data.Length);
        if (_sink?.TryWrite(samples) != true)
        {
            QueueStop();
        }
    }

    private void OnProgressChanged(object? sender, AudioCaptureProgress progress) =>
        ProgressChanged?.Invoke(this, progress);

    private void OnChunkCompleted(
        object? sender,
        AudioCaptureChunkCompletedEventArgs eventArgs) =>
        ChunkCompleted?.Invoke(this, eventArgs);

    private void OnSinkFaulted(
        object? sender,
        AudioCaptureFaultedEventArgs eventArgs)
    {
        CaptureFaulted?.Invoke(this, eventArgs);
        QueueStop();
    }

    private void QueueStop()
    {
        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                if (state is not MiniAudioCaptureSession session)
                {
                    return;
                }

                session._stopRequested = true;
                if (session.IsActive)
                {
                    session.Stop();
                }
            },
            this);
    }
}
