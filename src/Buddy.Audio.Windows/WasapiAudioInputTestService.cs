using System.Runtime.InteropServices;
using Buddy.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Buddy.Audio.Windows;

public sealed class WasapiAudioInputTestService : IAudioInputTestService
{
    public async Task<float> TestAsync(
        string? deviceId,
        TimeSpan duration,
        IProgress<float>? levelProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "A microphone test must last between 1 and 15 seconds.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using MMDevice device = GetDevice(deviceId);
        using WasapiCapture capture = new(
            device,
            useEventSync: true,
            audioBufferMillisecondsLength: 100)
        {
            ShareMode = AudioClientShareMode.Shared,
        };

        WaveFormat format = NormalizeFormat(capture.WaveFormat);
        AudioSampleEncoding encoding = GetEncoding(format);
        float maximumPeak = 0;
        TaskCompletionSource<Exception?> stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
        {
            if (eventArgs.BytesRecorded <= 0)
            {
                return;
            }

            float peak = AudioLevelMeter.GetPeak(
                eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded),
                format.BitsPerSample,
                encoding);
            maximumPeak = Math.Max(maximumPeak, peak);
            levelProgress?.Report(peak);
        }

        void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
        {
            stopped.TrySetResult(eventArgs.Exception);
        }

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        try
        {
            capture.StartRecording();
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (capture.CaptureState != CaptureState.Stopped)
            {
                capture.StopRecording();
            }

            levelProgress?.Report(0);
        }

        Exception? failure = await stopped.Task
            .WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None)
            .ConfigureAwait(false);
        if (failure is not null)
        {
            throw new IOException("The microphone test stopped unexpectedly.", failure);
        }

        return maximumPeak;
    }

    private static MMDevice GetDevice(string? deviceId)
    {
        using MMDeviceEnumerator enumerator = new();
        return string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.GetDevice(deviceId);
    }

    private static WaveFormat NormalizeFormat(WaveFormat format)
    {
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
}
