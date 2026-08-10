using System.Runtime.InteropServices;
using Buddy.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly ICaptureJournalStore _journals;

    public WasapiAudioCaptureService(ICaptureJournalStore journals)
    {
        _journals = journals ?? throw new ArgumentNullException(nameof(journals));
    }

    public Task<IReadOnlyList<AudioInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using MMDeviceEnumerator enumerator = new();
        string? defaultDeviceId = GetDefaultDeviceId(enumerator);
        MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(
            DataFlow.Capture,
            DeviceState.Active);
        List<AudioInputDevice> devices = new(endpoints.Count);

        foreach (MMDevice endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (endpoint)
            {
                devices.Add(new AudioInputDevice(
                    endpoint.ID,
                    endpoint.FriendlyName,
                    string.Equals(endpoint.ID, defaultDeviceId, StringComparison.Ordinal)));
            }
        }

        return Task.FromResult<IReadOnlyList<AudioInputDevice>>(
            devices
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }

    public async Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        MMDevice device = GetDevice(options.DeviceId);
        try
        {
            WasapiCapture capture = new(device, useEventSync: true, audioBufferMillisecondsLength: 100)
            {
                ShareMode = AudioClientShareMode.Shared,
            };

            WasapiAudioCaptureSession session = new(
                options,
                capture,
                device,
                _journals);

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
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private static MMDevice GetDevice(string? deviceId)
    {
        using MMDeviceEnumerator enumerator = new();
        return string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.GetDevice(deviceId);
    }

    private static string? GetDefaultDeviceId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Communications);
            return endpoint.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void ValidateOptions(AudioCaptureOptions options)
    {
        if (options.SessionId == Guid.Empty)
        {
            throw new ArgumentException("A capture session identifier is required.", nameof(options));
        }

        if (options.RecordingId == Guid.Empty)
        {
            throw new ArgumentException("A recording identifier is required.", nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.SessionDirectory);

        if (options.ChunkDuration < TimeSpan.FromSeconds(1)
            || options.ChunkDuration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ChunkDuration,
                "Chunk duration must be between one second and one minute.");
        }
    }
}
