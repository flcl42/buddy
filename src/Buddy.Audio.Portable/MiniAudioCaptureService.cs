using System.Globalization;
using Buddy.Core.Abstractions;
using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Core.StandardAPI;
using MiniAudioEx.Native;

namespace Buddy.Audio.Portable;

public sealed class MiniAudioCaptureService : IAudioCaptureService
{
    private readonly ICaptureJournalStore _journals;

    public MiniAudioCaptureService(ICaptureJournalStore journals)
    {
        _journals = journals ?? throw new ArgumentNullException(nameof(journals));
    }

    public Task<IReadOnlyList<AudioInputDevice>> GetInputDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioDevice[] devices = GetCaptureDevices();
        return Task.FromResult<IReadOnlyList<AudioInputDevice>>(
            devices
                .Select(
                    device => new AudioInputDevice(
                        GetId(device),
                        device.Name,
                        device.IsDefault))
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(
                    device => device.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }

    public async Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        cancellationToken.ThrowIfCancellationRequested();

        AudioDevice[] devices = GetCaptureDevices();
        AudioDevice device = SelectDevice(devices, options.DeviceId);
        MiniAudioCaptureSession session = new(options, device, _journals);
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

    internal static AudioDevice SelectDevice(
        IReadOnlyList<AudioDevice> devices,
        string? deviceId)
    {
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No microphone is available.");
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            AudioDevice? selected = devices.FirstOrDefault(
                device => string.Equals(
                    GetId(device),
                    deviceId,
                    StringComparison.Ordinal));
            return selected
                ?? throw new InvalidOperationException(
                    "The selected microphone is no longer available.");
        }

        return devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
    }

    internal static AudioDevice[] GetCaptureDevices()
    {
        using MaContext context = new();
        ma_result initialized = context.Initialize();
        if (initialized != ma_result.success)
        {
            throw new InvalidOperationException(
                $"Could not enumerate microphones ({initialized}).");
        }

        if (!context.GetDevices(out _, out MaDeviceInfo[]? captureDevices))
        {
            throw new InvalidOperationException("Could not enumerate microphones.");
        }

        return (captureDevices ?? [])
            .Select(
                device => new AudioDevice
                {
                    info = device.deviceInfo,
                })
            .ToArray();
    }

    internal static string GetId(AudioDevice device) =>
        device.info.id.GetHashCode().ToString("X8", CultureInfo.InvariantCulture)
        + ":"
        + device.Name;

    private static void Validate(AudioCaptureOptions options)
    {
        if (options.SessionId == Guid.Empty || options.RecordingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Capture session and recording identifiers are required.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.SessionDirectory);
        if (options.ChunkDuration < TimeSpan.FromSeconds(1)
            || options.ChunkDuration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Chunk duration must be between one second and one minute.");
        }

        if (options.PreferredSampleRate is < 8_000 or > 192_000
            || options.PreferredChannels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Portable capture supports one or two channels from 8 to 192 kHz.");
        }
    }
}
