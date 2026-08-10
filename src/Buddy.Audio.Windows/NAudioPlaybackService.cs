using System.Runtime.InteropServices;
using Buddy.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

public sealed class NAudioPlaybackService : IAudioPlaybackService
{
    private const int OutputLatencyMilliseconds = 120;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _progressTimer;
    private WasapiOut? _output;
    private MMDevice? _outputDevice;
    private ISeekablePlaybackSource? _source;
    private string? _selectedOutputDeviceId;
    private bool _disposed;

    public NAudioPlaybackService()
    {
        _progressTimer = new Timer(
            OnProgressTimer,
            null,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200));
    }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;

    public TimeSpan Position => _source?.Position ?? TimeSpan.Zero;

    public TimeSpan Duration => _source?.Duration ?? TimeSpan.Zero;

    public string? LoadedPath { get; private set; }

    public string? OutputDeviceName { get; private set; }

    public Exception? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using MMDeviceEnumerator enumerator = new();
        string? defaultDeviceId = GetDefaultDeviceId(enumerator);
        MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            DeviceState.Active);
        List<AudioOutputDevice> devices = new(endpoints.Count);

        foreach (MMDevice endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (endpoint)
            {
                devices.Add(new AudioOutputDevice(
                    endpoint.ID,
                    endpoint.FriendlyName,
                    string.Equals(
                        endpoint.ID,
                        defaultDeviceId,
                        StringComparison.Ordinal)));
            }
        }

        return Task.FromResult<IReadOnlyList<AudioOutputDevice>>(
            devices
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
            if (IsPlaying || IsPaused)
            {
                throw new InvalidOperationException(
                    "Stop the current audio before changing Buddy's speaker.");
            }

            using (MMDevice selected = GetOutputDevice(deviceId, allowDefaultFallback: false))
            {
                OutputDeviceName = selected.FriendlyName;
            }

            _selectedOutputDeviceId = string.IsNullOrWhiteSpace(deviceId)
                ? null
                : deviceId;
            if (_source is not null)
            {
                DisposeOutput();
                CreateOutput();
            }
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsPlaying || IsPaused)
            {
                throw new InvalidOperationException(
                    "Stop the current audio before testing another speaker.");
            }

            using MMDevice device = GetOutputDevice(deviceId, allowDefaultFallback: false);
            using WasapiOut output = new(
                device,
                AudioClientShareMode.Shared,
                useEventSync: true,
                OutputLatencyMilliseconds);
            using WaveStream tone = CreateOutputTestTone();
            TaskCompletionSource completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? playbackError = null;

            void HandleStopped(object? sender, StoppedEventArgs eventArgs)
            {
                playbackError = eventArgs.Exception;
                completion.TrySetResult();
            }

            output.PlaybackStopped += HandleStopped;
            try
            {
                output.Init(tone);
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(output.Stop);
                output.Play();
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                output.PlaybackStopped -= HandleStopped;
            }

            if (playbackError is not null)
            {
                throw new InvalidOperationException(
                    $"Windows stopped the speaker test on {device.FriendlyName}.",
                    playbackError);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The recording audio file was not found.", fullPath);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            DisposePlayback();
            LastError = null;
            try
            {
                _source = string.Equals(
                        Path.GetExtension(fullPath),
                        ".wav",
                        StringComparison.OrdinalIgnoreCase)
                    ? new WavePlaybackSource(fullPath)
                    : new OpusOggPlaybackSource(fullPath);
                LoadedPath = fullPath;
                CreateOutput();
            }
            catch
            {
                DisposePlayback();
                throw;
            }
        }
        finally
        {
            _gate.Release();
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
            if (_source!.Duration > TimeSpan.Zero
                && _source.Position >= _source.Duration - TimeSpan.FromMilliseconds(20))
            {
                DisposeOutput();
                _source.Seek(TimeSpan.Zero);
                CreateOutput();
            }

            _output!.Play();
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
            _output!.Pause();
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
            LastError = null;
            DisposeOutput();
            _source!.Seek(TimeSpan.Zero);
            CreateOutput();
            _output!.Play();
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
            bool resume = _output!.PlaybackState == PlaybackState.Playing;
            DisposeOutput();
            _source!.Seek(position);
            CreateOutput();
            if (resume)
            {
                _output!.Play();
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
            EnsureLoaded();
            DisposeOutput();
            _source!.Seek(TimeSpan.Zero);
            CreateOutput();
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
            await _progressTimer.DisposeAsync().ConfigureAwait(false);
            DisposePlayback();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    internal static WaveStream CreateOutputTestTone()
    {
        const int sampleRate = 48_000;
        const double durationSeconds = 1.2;
        const double amplitude = 0.28;
        const double fadeSeconds = 0.025;
        int frameCount = (int)(sampleRate * durationSeconds);
        byte[] pcm = new byte[frameCount * sizeof(short)];

        for (int frame = 0; frame < frameCount; frame++)
        {
            double time = (double)frame / sampleRate;
            double toneTime;
            double toneDuration;
            double frequency;
            if (time < 0.48)
            {
                toneTime = time;
                toneDuration = 0.48;
                frequency = 523.25;
            }
            else if (time >= 0.62 && time < 1.1)
            {
                toneTime = time - 0.62;
                toneDuration = 0.48;
                frequency = 659.25;
            }
            else
            {
                continue;
            }

            double envelope = Math.Min(
                1,
                Math.Min(toneTime / fadeSeconds, (toneDuration - toneTime) / fadeSeconds));
            double sample = amplitude
                * Math.Max(0, envelope)
                * Math.Sin(2 * Math.PI * frequency * toneTime);
            short encoded = (short)Math.Round(sample * short.MaxValue);
            pcm[frame * sizeof(short)] = (byte)(encoded & 0xff);
            pcm[(frame * sizeof(short)) + 1] = (byte)((encoded >> 8) & 0xff);
        }

        return new RawSourceWaveStream(
            new MemoryStream(pcm, writable: false),
            new WaveFormat(sampleRate, bits: 16, channels: 1));
    }

    private void CreateOutput()
    {
        if (_source is null)
        {
            throw new InvalidOperationException("Load audio before creating its output.");
        }

        MMDevice device = GetOutputDevice(
            _selectedOutputDeviceId,
            allowDefaultFallback: true);
        WasapiOut? output = null;
        try
        {
            output = new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                useEventSync: true,
                OutputLatencyMilliseconds);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(_source);
            _outputDevice = device;
            _output = output;
            OutputDeviceName = device.FriendlyName;
        }
        catch
        {
            if (output is not null)
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Dispose();
            }

            device.Dispose();
            throw;
        }
    }

    private void DisposeOutput()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _outputDevice?.Dispose();
        _outputDevice = null;
    }

    private void DisposePlayback()
    {
        DisposeOutput();
        _source?.Dispose();
        _source = null;
        LoadedPath = null;
    }

    private void EnsureLoaded()
    {
        if (_source is null || _output is null)
        {
            throw new InvalidOperationException("Load a recording before controlling playback.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static MMDevice GetOutputDevice(
        string? deviceId,
        bool allowDefaultFallback)
    {
        using MMDeviceEnumerator enumerator = new();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            MMDevice? selected = null;
            try
            {
                selected = enumerator.GetDevice(deviceId);
                if (selected.State == DeviceState.Active)
                {
                    MMDevice active = selected;
                    selected = null;
                    return active;
                }
            }
            catch (COMException) when (allowDefaultFallback)
            {
            }
            finally
            {
                selected?.Dispose();
            }

            if (!allowDefaultFallback)
            {
                throw new InvalidOperationException(
                    "The selected speaker is not currently available.");
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static string? GetDefaultDeviceId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia);
            return endpoint.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            LastError = eventArgs.Exception;
        }

        OnStateChanged();
    }

    private void OnProgressTimer(object? state)
    {
        if (IsPlaying)
        {
            OnStateChanged();
        }
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
