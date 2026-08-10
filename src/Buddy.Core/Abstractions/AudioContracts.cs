using Buddy.Core.Domain;

namespace Buddy.Core.Abstractions;

public sealed record AudioInputDevice(
    string Id,
    string DisplayName,
    bool IsDefault);

public sealed record AudioOutputDevice(
    string Id,
    string DisplayName,
    bool IsDefault);

public enum AudioSampleEncoding
{
    Pcm = 0,
    IeeeFloat = 1,
}

public sealed record AudioCaptureOptions(
    Guid SessionId,
    Guid RecordingId,
    RecordingKind Kind,
    string SessionDirectory,
    string? DeviceId,
    TimeSpan ChunkDuration,
    int PreferredSampleRate = 48_000,
    int PreferredChannels = 1);

public sealed record AudioCaptureProgress(
    TimeSpan Duration,
    float Peak,
    long PcmBytes);

public sealed record AudioCaptureChunk(
    Guid RecordingId,
    int Sequence,
    string Path,
    TimeSpan Start,
    TimeSpan Duration,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    AudioSampleEncoding Encoding,
    long ByteLength);

public sealed class AudioCaptureChunkCompletedEventArgs : EventArgs
{
    public AudioCaptureChunkCompletedEventArgs(AudioCaptureChunk chunk)
    {
        Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
    }

    public AudioCaptureChunk Chunk { get; }
}

public sealed class AudioCaptureFaultedEventArgs : EventArgs
{
    public AudioCaptureFaultedEventArgs(Exception error)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public Exception Error { get; }
}

public sealed record AudioCaptureResult(
    Guid SessionId,
    Guid RecordingId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string InputDeviceId,
    string InputDeviceName,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    AudioSampleEncoding Encoding,
    IReadOnlyList<string> ChunkPaths,
    long TotalPcmBytes);

public interface IAudioCaptureSession : IAsyncDisposable
{
    Guid SessionId { get; }

    Guid RecordingId { get; }

    bool IsRecording { get; }

    event EventHandler<AudioCaptureProgress>? ProgressChanged;

    event EventHandler<AudioCaptureChunkCompletedEventArgs>? ChunkCompleted;

    event EventHandler<AudioCaptureFaultedEventArgs>? CaptureFaulted;

    Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default);
}

public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken = default);
}

public interface IAudioInputTestService
{
    Task<float> TestAsync(
        string? deviceId,
        TimeSpan duration,
        IProgress<float>? levelProgress = null,
        CancellationToken cancellationToken = default);
}

public interface IAudioArchiveService
{
    Task<AudioArtifact> CreateOriginalArchiveAsync(
        AudioCaptureResult capture,
        string destinationPath,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<AudioArtifact> CreateCompactArchiveAsync(
        Guid recordingId,
        string sourcePath,
        string destinationPath,
        IReadOnlyList<SpeechSegment> segments,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}

public sealed record PreparedSpeechAudio(
    string Path,
    int SampleRate,
    int Channels,
    TimeSpan Duration,
    long ByteLength);

public interface IAudioPreparationService
{
    Task<PreparedSpeechAudio> CreateSpeechWaveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<PreparedSpeechAudio> CreateSpeechWaveAsync(
        AudioCaptureResult capture,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public interface IAudioPlaybackService : IAsyncDisposable
{
    bool IsPlaying { get; }

    bool IsPaused { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    string? LoadedPath { get; }

    string? OutputDeviceName { get; }

    Exception? LastError { get; }

    event EventHandler? StateChanged;

    Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(
        CancellationToken cancellationToken = default);

    Task SetOutputDeviceAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task TestOutputAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task LoadAsync(string path, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IAudioWaveformService
{
    Task<AudioWaveform> CreateAsync(
        Guid artifactId,
        string path,
        int sampleCount = AudioWaveform.DefaultSampleCount,
        CancellationToken cancellationToken = default);
}
