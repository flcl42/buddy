using Buddy.Core.Abstractions;
using MiniAudioEx.Core.StandardAPI;
using MiniAudioEx.Native;

namespace Buddy.Audio.Portable;

public sealed class MiniAudioInputTestService : IAudioInputTestService
{
    public async Task<float> TestAsync(
        string? deviceId,
        TimeSpan duration,
        IProgress<float>? levelProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromSeconds(1)
            || duration > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "A microphone test must last between 1 and 15 seconds.");
        }

        AudioDevice[] devices = MiniAudioCaptureService.GetCaptureDevices();
        AudioDevice device = MiniAudioCaptureService.SelectDevice(devices, deviceId);
        using PeakRecorder recorder = new(device, levelProgress);
        if (!recorder.Initialize() || !recorder.Start())
        {
            throw new InvalidOperationException(
                $"Could not test microphone {device.Name}.");
        }

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (recorder.IsActive)
            {
                recorder.Stop();
            }

            levelProgress?.Report(0);
        }

        return recorder.MaximumPeak;
    }

    private sealed class PeakRecorder : AudioRecorder
    {
        private readonly IProgress<float>? _progress;
        private float _maximumPeak;

        internal PeakRecorder(
            AudioDevice device,
            IProgress<float>? progress)
            : base(48_000, 1)
        {
            _progress = progress;
            SetDevice(device);
        }

        internal float MaximumPeak => _maximumPeak;

        protected override bool OnStart() => true;

        protected override void OnProcess(
            NativeArray<float> data,
            uint frameCount)
        {
            float peak = 0;
            for (int index = 0; index < data.Length; index++)
            {
                float value = Math.Abs(data[index]);
                if (float.IsFinite(value))
                {
                    peak = Math.Max(peak, Math.Min(1, value));
                }
            }

            _maximumPeak = Math.Max(_maximumPeak, peak);
            _progress?.Report(peak);
        }
    }
}
