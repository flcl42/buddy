using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AVFoundation;
using Buddy.Audio.Portable;
using Buddy.Core.Abstractions;
using Foundation;

namespace Buddy.App.Platforms.MacCatalyst;

[SupportedOSPlatform("maccatalyst15.0")]
public sealed class MacCatalystAudioCaptureService : IAudioCaptureService
{
    private const string DefaultDeviceId = "system-default";
    private readonly ICaptureJournalStore _journals;

    public MacCatalystAudioCaptureService(ICaptureJournalStore journals)
    {
        _journals = journals ?? throw new ArgumentNullException(nameof(journals));
    }

    public Task<IReadOnlyList<AudioInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AudioInputDevice>>(
            [new AudioInputDevice(DefaultDeviceId, "macOS system microphone", true)]);
    }

    public async Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateDevice(options.DeviceId);
        PermissionStatus permission = await Permissions
            .RequestAsync<Permissions.Microphone>()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (permission != PermissionStatus.Granted)
        {
            throw new UnauthorizedAccessException(
                "Microphone permission is required to record audio.");
        }

        MacCatalystAudioCaptureSession session = new(options, _journals);
        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static void ValidateDevice(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId)
            && !string.Equals(deviceId, DefaultDeviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Select the microphone in macOS Sound settings; Buddy follows the system input.");
        }
    }
}

[SupportedOSPlatform("maccatalyst15.0")]
internal sealed class MacCatalystAudioCaptureSession : IAudioCaptureSession
{
    private const string DeviceId = "system-default";
    private const string DeviceName = "macOS system microphone";
    private readonly AudioCaptureOptions _options;
    private readonly ICaptureJournalStore _journals;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private AVAudioEngine? _engine;
    private AVAudioInputNode? _input;
    private FloatPcmCaptureSink? _sink;
    private Exception? _captureFailure;
    private bool _tapInstalled;
    private bool _stopRequested;
    private bool _disposed;

    internal MacCatalystAudioCaptureSession(
        AudioCaptureOptions options,
        ICaptureJournalStore journals)
    {
        _options = options;
        _journals = journals;
    }

    public Guid SessionId => _options.SessionId;

    public Guid RecordingId => _options.RecordingId;

    public bool IsRecording => !_stopRequested && _engine?.Running == true;

    public event EventHandler<AudioCaptureProgress>? ProgressChanged;

    public event EventHandler<AudioCaptureChunkCompletedEventArgs>? ChunkCompleted;

    public event EventHandler<AudioCaptureFaultedEventArgs>? CaptureFaulted;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AVAudioEngine engine = new();
        AVAudioInputNode input = engine.InputNode;
        AVAudioFormat format = input.GetBusOutputFormat(0);
        int sampleRate = checked((int)Math.Round(format.SampleRate));
        int channels = checked((int)format.ChannelCount);
        if (!format.Standard
            || format.CommonFormat != AVAudioCommonFormat.PCMFloat32
            || sampleRate is < 8_000 or > 192_000
            || channels is < 1 or > 2)
        {
            engine.Dispose();
            throw new NotSupportedException(
                "The macOS system microphone did not expose mono/stereo Float32 PCM.");
        }

        FloatPcmCaptureSink sink = new(
            _options,
            DeviceId,
            DeviceName,
            sampleRate,
            channels,
            _journals);
        sink.ProgressChanged += OnProgressChanged;
        sink.ChunkCompleted += OnChunkCompleted;
        sink.CaptureFaulted += OnSinkFaulted;
        await sink.StartAsync(cancellationToken).ConfigureAwait(false);

        _engine = engine;
        _input = input;
        _sink = sink;
        input.InstallTapOnBus(
            0,
            2_048,
            format,
            (buffer, _) => CaptureBuffer(buffer, channels, format.Interleaved));
        _tapInstalled = true;
        engine.Prepare();
        if (!engine.StartAndReturnError(out NSError? error))
        {
            throw new InvalidOperationException(
                error?.LocalizedDescription
                    ?? "macOS could not start microphone capture.");
        }
    }

    public async Task<AudioCaptureResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _stopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopRequested && _sink is null)
            {
                throw new InvalidOperationException("Microphone capture is not active.");
            }

            _stopRequested = true;
            StopNativeCapture();
            FloatPcmCaptureSink sink = _sink
                ?? throw new InvalidOperationException("Microphone capture is not active.");
            AudioCaptureResult result = await sink
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
            if (_captureFailure is not null)
            {
                throw new IOException(
                    "macOS microphone capture failed.",
                    _captureFailure);
            }

            return result;
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

        if (!_stopRequested && _sink is not null)
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (
                error is IOException or InvalidOperationException)
            {
            }
        }

        _disposed = true;
        StopNativeCapture();
        if (_sink is not null)
        {
            _sink.ProgressChanged -= OnProgressChanged;
            _sink.ChunkCompleted -= OnChunkCompleted;
            _sink.CaptureFaulted -= OnSinkFaulted;
            await _sink.DisposeAsync().ConfigureAwait(false);
            _sink = null;
        }

        _input?.Dispose();
        _engine?.Dispose();
        _input = null;
        _engine = null;
        _stopGate.Dispose();
    }

    private unsafe void CaptureBuffer(
        AVAudioPcmBuffer buffer,
        int channels,
        bool interleaved)
    {
        try
        {
            int frames = checked((int)buffer.FrameLength);
            if (frames == 0 || buffer.FloatChannelData == IntPtr.Zero)
            {
                return;
            }

            IntPtr firstChannel = Marshal.ReadIntPtr(buffer.FloatChannelData);
            if (interleaved || channels == 1)
            {
                ReadOnlySpan<float> samples = new(
                    firstChannel.ToPointer(),
                    checked(frames * channels));
                _ = _sink?.TryWrite(samples);
                return;
            }

            float[] rented = ArrayPool<float>.Shared.Rent(
                checked(frames * channels));
            try
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    IntPtr channelPointer = Marshal.ReadIntPtr(
                        buffer.FloatChannelData,
                        channel * IntPtr.Size);
                    ReadOnlySpan<float> source = new(
                        channelPointer.ToPointer(),
                        frames);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        rented[(frame * channels) + channel] = source[frame];
                    }
                }

                _ = _sink?.TryWrite(rented.AsSpan(0, frames * channels));
            }
            finally
            {
                ArrayPool<float>.Shared.Return(rented);
            }
        }
        catch (Exception error) when (
            error is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            _captureFailure ??= error;
            CaptureFaulted?.Invoke(this, new AudioCaptureFaultedEventArgs(error));
            StopNativeCapture();
        }
    }

    private void StopNativeCapture()
    {
        if (_tapInstalled && _input is not null)
        {
            _input.RemoveTapOnBus(0);
            _tapInstalled = false;
        }

        if (_engine?.Running == true)
        {
            _engine.Stop();
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
        _captureFailure ??= eventArgs.Error;
        CaptureFaulted?.Invoke(this, eventArgs);
        StopNativeCapture();
    }
}

[SupportedOSPlatform("maccatalyst15.0")]
public sealed class MacCatalystAudioInputTestService : IAudioInputTestService
{
    public async Task<float> TestAsync(
        string? deviceId,
        TimeSpan duration,
        IProgress<float>? levelProgress = null,
        CancellationToken cancellationToken = default)
    {
        MacCatalystAudioCaptureService.ValidateDevice(deviceId);
        if (duration < TimeSpan.FromSeconds(1)
            || duration > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        PermissionStatus permission = await Permissions
            .RequestAsync<Permissions.Microphone>()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (permission != PermissionStatus.Granted)
        {
            throw new UnauthorizedAccessException(
                "Microphone permission is required to test audio input.");
        }

        using AVAudioEngine engine = new();
        AVAudioInputNode input = engine.InputNode;
        AVAudioFormat format = input.GetBusOutputFormat(0);
        float maximum = 0;
        input.InstallTapOnBus(
            0,
            2_048,
            format,
            (buffer, _) =>
            {
                float peak = ReadPeak(
                    buffer,
                    checked((int)format.ChannelCount),
                    format.Interleaved);
                maximum = Math.Max(maximum, peak);
                levelProgress?.Report(peak);
            });
        try
        {
            engine.Prepare();
            if (!engine.StartAndReturnError(out NSError? error))
            {
                throw new InvalidOperationException(
                    error?.LocalizedDescription
                        ?? "macOS could not start the microphone test.");
            }

            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            return maximum;
        }
        finally
        {
            input.RemoveTapOnBus(0);
            if (engine.Running)
            {
                engine.Stop();
            }

            levelProgress?.Report(0);
            input.Dispose();
        }
    }

    private static unsafe float ReadPeak(
        AVAudioPcmBuffer buffer,
        int channels,
        bool interleaved)
    {
        int frames = checked((int)buffer.FrameLength);
        if (frames == 0 || buffer.FloatChannelData == IntPtr.Zero)
        {
            return 0;
        }

        float peak = 0;
        if (interleaved || channels == 1)
        {
            IntPtr samplesPointer = Marshal.ReadIntPtr(buffer.FloatChannelData);
            ReadOnlySpan<float> samples = new(
                samplesPointer.ToPointer(),
                checked(frames * channels));
            return ReadPeak(samples, peak);
        }

        for (int channel = 0; channel < channels; channel++)
        {
            IntPtr channelPointer = Marshal.ReadIntPtr(
                buffer.FloatChannelData,
                channel * IntPtr.Size);
            ReadOnlySpan<float> samples = new(channelPointer.ToPointer(), frames);
            peak = ReadPeak(samples, peak);
        }

        return peak;
    }

    private static float ReadPeak(ReadOnlySpan<float> samples, float peak)
    {
        foreach (float sample in samples)
        {
            if (float.IsFinite(sample))
            {
                peak = Math.Max(peak, Math.Min(1, Math.Abs(sample)));
            }
        }

        return peak;
    }
}

[SupportedOSPlatform("maccatalyst15.0")]
public sealed class MacCatalystAudioPlaybackService : IAudioPlaybackService
{
    private const string DefaultDeviceId = "system-default";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _timer;
    private AVAudioPlayer? _player;
    private string? _temporaryWavePath;
    private bool _isPaused;
    private bool _disposed;

    public MacCatalystAudioPlaybackService()
    {
        _timer = new Timer(
            OnTimer,
            null,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(150));
    }

    public bool IsPlaying => _player?.Playing == true;

    public bool IsPaused => _isPaused;

    public TimeSpan Position { get; private set; }

    public TimeSpan Duration { get; private set; }

    public string? LoadedPath { get; private set; }

    public string? OutputDeviceName => "macOS system output";

    public Exception? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AudioOutputDevice>>(
            [new AudioOutputDevice(DefaultDeviceId, "macOS system output", true)]);
    }

    public Task SetOutputDeviceAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDevice(deviceId);
        return Task.CompletedTask;
    }

    public async Task TestOutputAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        ValidateDevice(deviceId);
        string path = await PortableAudioFilePreparation
            .CreateTestToneAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using AVAudioPlayer player = CreatePlayer(path);
            _ = player.Play();
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(4);
            while (player.Playing && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            player.Stop();
        }
        finally
        {
            PortableAudioFilePreparation.TryDelete(path);
        }
    }

    public async Task LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        PreparedPlaybackFile prepared = await PortableAudioFilePreparation
            .PrepareWaveAsync(path, cancellationToken)
            .ConfigureAwait(false);
        bool accepted = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                DisposePlayer();
                _player = CreatePlayer(prepared.Path);
                _player.FinishedPlaying += OnFinishedPlaying;
                _ = _player.PrepareToPlay();
                _temporaryWavePath = prepared.IsTemporary ? prepared.Path : null;
                LoadedPath = Path.GetFullPath(path);
                Duration = prepared.Duration;
                Position = TimeSpan.Zero;
                _isPaused = false;
                LastError = null;
                accepted = true;
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            if (!accepted && prepared.IsTemporary)
            {
                PortableAudioFilePreparation.TryDelete(prepared.Path);
            }
        }

        OnStateChanged();
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            AVAudioPlayer player = EnsurePlayer();
            if (player.CurrentTime >= player.Duration - 0.02)
            {
                player.CurrentTime = 0;
            }

            if (!player.Play())
            {
                throw new InvalidOperationException("macOS could not start audio playback.");
            }

            _isPaused = false;
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AVAudioPlayer player = EnsurePlayer();
            player.Pause();
            Position = TimeSpan.FromSeconds(player.CurrentTime);
            _isPaused = true;
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await SeekAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        await PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AVAudioPlayer player = EnsurePlayer();
            TimeSpan target = position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > Duration
                    ? Duration
                    : position;
            player.CurrentTime = target.TotalSeconds;
            Position = target;
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_player is not null)
            {
                _player.Stop();
                _player.CurrentTime = 0;
            }

            Position = TimeSpan.Zero;
            _isPaused = false;
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            DisposePlayer();
        }
        finally
        {
            _gate.Release();
        }

        _timer.Dispose();
        _gate.Dispose();
    }

    private static AVAudioPlayer CreatePlayer(string path)
    {
        AVAudioPlayer? player = AVAudioPlayer.FromUrl(
            NSUrl.FromFilename(path),
            out NSError? error);
        return player ?? throw new InvalidDataException(
            error?.LocalizedDescription ?? "macOS could not open this audio file.");
    }

    private static void ValidateDevice(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId)
            && !string.Equals(deviceId, DefaultDeviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Select the speaker in macOS Sound settings; Buddy follows the system output.");
        }
    }

    private AVAudioPlayer EnsurePlayer() => _player
        ?? throw new InvalidOperationException("Load audio before playing it.");

    private void DisposePlayer()
    {
        if (_player is not null)
        {
            _player.FinishedPlaying -= OnFinishedPlaying;
            _player.Stop();
            _player.Dispose();
            _player = null;
        }

        PortableAudioFilePreparation.TryDelete(_temporaryWavePath);
        _temporaryWavePath = null;
        LoadedPath = null;
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        _isPaused = false;
    }

    private void OnFinishedPlaying(object? sender, AVStatusEventArgs eventArgs)
    {
        Position = Duration;
        _isPaused = false;
        OnStateChanged();
    }

    private void OnTimer(object? state)
    {
        if (_disposed || !_gate.Wait(0))
        {
            return;
        }

        bool changed = false;
        try
        {
            if (_player?.Playing == true)
            {
                Position = TimeSpan.FromSeconds(_player.CurrentTime);
                changed = true;
            }
        }
        catch (ObjectDisposedException error)
        {
            LastError = error;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            OnStateChanged();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
