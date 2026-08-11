using System.Globalization;
using Buddy.Core.Abstractions;
using MiniAudioEx.Core.StandardAPI;
using NAudio.Wave;

namespace Buddy.Audio.Portable;

public sealed class MiniAudioPlaybackService : IAudioPlaybackService
{
    private const int OutputSampleRate = 48_000;
    private const int OutputChannels = 2;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _updateTimer;
    private AudioSource? _source;
    private AudioClip? _clip;
    private string? _preparedPath;
    private string? _temporaryWavePath;
    private string? _selectedOutputDeviceId;
    private int _sourceSampleRate = OutputSampleRate;
    private bool _contextInitialized;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _sourceEnded;
    private bool _disposed;

    public MiniAudioPlaybackService()
    {
        _updateTimer = new Timer(
            OnUpdateTimer,
            null,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(150));
    }

    public bool IsPlaying => _isPlaying;

    public bool IsPaused => _isPaused;

    public TimeSpan Position { get; private set; }

    public TimeSpan Duration { get; private set; }

    public string? LoadedPath { get; private set; }

    public string? OutputDeviceName { get; private set; }

    public Exception? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeviceInfo[] devices = AudioContext.GetDevices() ?? [];
        return Task.FromResult<IReadOnlyList<AudioOutputDevice>>(
            devices
                .Select(
                    device => new AudioOutputDevice(
                        GetId(device),
                        device.Name,
                        device.IsDefault))
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(
                    device => device.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }

    public async Task SetOutputDeviceAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isPlaying || _isPaused)
            {
                throw new InvalidOperationException(
                    "Stop the current audio before changing Buddy's speaker.");
            }

            DeviceInfo selected = SelectOutputDevice(deviceId);
            _selectedOutputDeviceId = string.IsNullOrWhiteSpace(deviceId)
                ? null
                : deviceId;
            ReinitializeContext(selected);
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task TestOutputAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        string tonePath = Path.Combine(
            CreateTemporaryDirectory(),
            $"speaker-test-{Guid.NewGuid():N}.wav");
        CreateTestTone(tonePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? previousDevice = _selectedOutputDeviceId;
        try
        {
            ThrowIfDisposed();
            if (_isPlaying || _isPaused)
            {
                throw new InvalidOperationException(
                    "Stop the current audio before testing another speaker.");
            }

            DeviceInfo selected = SelectOutputDevice(deviceId);
            ReinitializeContext(selected);
            using AudioClip clip = new(tonePath, streamFromDisk: true);
            using AudioSource source = new(maxSources: 1)
            {
                Spatial = false,
            };
            TaskCompletionSource completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            source.End += completion.SetResult;
            source.Play(clip);
            _gate.Release();
            try
            {
                await completion.Task
                    .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                source.Stop();
            }

            ReinitializeContext(SelectOutputDevice(previousDevice));
        }
        finally
        {
            _gate.Release();
            TryDelete(tonePath);
        }

        OnStateChanged();
    }

    public async Task LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The recording audio file was not found.",
                fullPath);
        }

        PreparedPlayback prepared = await Task.Run(
                () => PreparePlayback(fullPath, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        bool accepted = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                DisposeLoadedAudio();
                EnsureContext();
                LoadedPath = fullPath;
                _preparedPath = prepared.Path;
                _temporaryWavePath = prepared.IsTemporary
                    ? prepared.Path
                    : null;
                Duration = prepared.Duration;
                _sourceSampleRate = prepared.SampleRate;
                CreateSource();
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
                TryDelete(prepared.Path);
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
            EnsureLoaded();
            LastError = null;
            if (Position >= Duration - TimeSpan.FromMilliseconds(20))
            {
                SetCursor(TimeSpan.Zero);
            }

            _sourceEnded = false;
            _source!.Play(_clip!);
            _isPlaying = true;
            _isPaused = false;
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or IOException)
        {
            LastError = error;
            throw;
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
            ThrowIfDisposed();
            EnsureLoaded();
            if (_isPlaying)
            {
                UpdatePosition();
                _source!.Stop();
                _isPlaying = false;
                _isPaused = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureLoaded();
            _source!.Stop();
            SetCursor(TimeSpan.Zero);
            _sourceEnded = false;
            _source.Play(_clip!);
            _isPlaying = true;
            _isPaused = false;
        }
        finally
        {
            _gate.Release();
        }

        OnStateChanged();
    }

    public async Task SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureLoaded();
            bool resume = _isPlaying;
            if (resume)
            {
                _source!.Stop();
            }

            SetCursor(position);
            _sourceEnded = false;
            if (resume)
            {
                _source!.Play(_clip!);
            }
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
            ThrowIfDisposed();
            if (_source is not null)
            {
                _source.Stop();
                SetCursor(TimeSpan.Zero);
            }

            _sourceEnded = false;
            _isPlaying = false;
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
            DisposeLoadedAudio();
            if (_contextInitialized)
            {
                AudioContext.Deinitialize();
                _contextInitialized = false;
            }
        }
        finally
        {
            _gate.Release();
        }

        _updateTimer.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string GetId(DeviceInfo device) =>
        device.Index.ToString(CultureInfo.InvariantCulture);

    private static DeviceInfo SelectOutputDevice(string? deviceId)
    {
        DeviceInfo[] devices = AudioContext.GetDevices() ?? [];
        if (devices.Length == 0)
        {
            throw new InvalidOperationException("No speaker is available.");
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            DeviceInfo? selected = devices.FirstOrDefault(
                device => string.Equals(
                    GetId(device),
                    deviceId,
                    StringComparison.Ordinal));
            return selected
                ?? throw new InvalidOperationException(
                    "The selected speaker is no longer available.");
        }

        return devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
    }

    private void EnsureContext()
    {
        if (!_contextInitialized)
        {
            InitializeContext(SelectOutputDevice(_selectedOutputDeviceId));
        }
    }

    private void InitializeContext(DeviceInfo device)
    {
        AudioContext.Initialize(
            OutputSampleRate,
            OutputChannels,
            periodSizeInFrames: 0,
            device);
        _contextInitialized = true;
        OutputDeviceName = device.Name;
    }

    private void ReinitializeContext(DeviceInfo device)
    {
        string? preparedPath = _preparedPath;
        bool hadLoadedAudio = preparedPath is not null;
        DisposeSource();
        if (_contextInitialized)
        {
            AudioContext.Deinitialize();
            _contextInitialized = false;
        }

        InitializeContext(device);
        if (hadLoadedAudio)
        {
            CreateSource();
        }
    }

    private void CreateSource()
    {
        if (_preparedPath is null)
        {
            return;
        }

        _clip = new AudioClip(_preparedPath, streamFromDisk: true);
        _source = new AudioSource(maxSources: 1)
        {
            Spatial = false,
            Volume = 0,
        };
        _source.End += OnSourceEnded;
        _source.Play(_clip);
        _source.Stop();
        _source.Cursor = 0;
        _source.Volume = 1;
        Position = TimeSpan.Zero;
        _isPlaying = false;
        _isPaused = false;
        _sourceEnded = false;
    }

    private void DisposeSource()
    {
        if (_source is not null)
        {
            _source.End -= OnSourceEnded;
            _source.Stop();
            _source.Dispose();
            _source = null;
        }

        _clip?.Dispose();
        _clip = null;
        _isPlaying = false;
        _isPaused = false;
        Position = TimeSpan.Zero;
    }

    private void DisposeLoadedAudio()
    {
        DisposeSource();
        string? temporary = _temporaryWavePath;
        _temporaryWavePath = null;
        _preparedPath = null;
        LoadedPath = null;
        Duration = TimeSpan.Zero;
        if (temporary is not null)
        {
            TryDelete(temporary);
        }
    }

    private void SetCursor(TimeSpan position)
    {
        TimeSpan target = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > Duration
                ? Duration
                : position;
        _source!.Cursor = checked((ulong)Math.Max(
            0,
            Math.Round(target.TotalSeconds * _sourceSampleRate)));
        Position = target;
    }

    private void UpdatePosition()
    {
        if (_source is null)
        {
            Position = TimeSpan.Zero;
            return;
        }

        TimeSpan position = TimeSpan.FromSeconds(
            _source.Cursor / (double)_sourceSampleRate);
        Position = position > Duration ? Duration : position;
    }

    private void OnSourceEnded()
    {
        _sourceEnded = true;
    }

    private void OnUpdateTimer(object? state)
    {
        if (_disposed || !_gate.Wait(0))
        {
            return;
        }

        bool changed = false;
        try
        {
            if (!_contextInitialized)
            {
                return;
            }

            AudioContext.Update();
            if (_source is not null)
            {
                UpdatePosition();
                changed = _isPlaying;
            }

            if (_sourceEnded)
            {
                _sourceEnded = false;
                Position = Duration;
                _isPlaying = false;
                _isPaused = false;
                changed = true;
            }
        }
        catch (Exception error) when (
            error is InvalidOperationException
                or IOException)
        {
            LastError = error;
            _isPlaying = false;
            _isPaused = false;
            changed = true;
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

    private static PreparedPlayback PreparePlayback(
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetExtension(fullPath),
                ".wav",
                StringComparison.OrdinalIgnoreCase))
        {
            using WaveFileReader reader = new(fullPath);
            if (reader.TotalTime <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    "The WAV file does not contain playable audio.");
            }

            return new PreparedPlayback(
                fullPath,
                reader.TotalTime,
                reader.WaveFormat.SampleRate,
                false);
        }

        string destination = Path.Combine(
            CreateTemporaryDirectory(),
            $"playback-{Guid.NewGuid():N}.wav");
        try
        {
            using Buddy.Audio.Windows.OpusOggPlaybackSource source = new(fullPath);
            using WaveFileWriter writer = new(destination, source.WaveFormat);
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                writer.Write(buffer, 0, read);
            }

            return new PreparedPlayback(
                destination,
                source.Duration,
                source.WaveFormat.SampleRate,
                true);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    private static void CreateTestTone(string path)
    {
        WaveFormat format = new(OutputSampleRate, 16, 1);
        using WaveFileWriter writer = new(path, format);
        int sampleCount = (int)(OutputSampleRate * 0.7);
        for (int index = 0; index < sampleCount; index++)
        {
            double fade = Math.Min(1, index / (OutputSampleRate * 0.04));
            fade = Math.Min(
                fade,
                (sampleCount - index) / (OutputSampleRate * 0.04));
            short sample = (short)Math.Round(
                Math.Sin(2 * Math.PI * 660 * index / OutputSampleRate)
                * short.MaxValue
                * 0.16
                * fade);
            writer.WriteByte((byte)(sample & 0xFF));
            writer.WriteByte((byte)((sample >> 8) & 0xFF));
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Buddy", "playback");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void EnsureLoaded()
    {
        if (_source is null || _clip is null)
        {
            throw new InvalidOperationException("Load audio before playing it.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record PreparedPlayback(
        string Path,
        TimeSpan Duration,
        int SampleRate,
        bool IsTemporary);
}
