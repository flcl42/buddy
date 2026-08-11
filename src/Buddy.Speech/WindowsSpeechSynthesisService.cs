using System.Runtime.InteropServices.WindowsRuntime;
using Buddy.Core.Abstractions;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace Buddy.Speech;

public sealed class WindowsSpeechSynthesisService : IPlatformSpeechSynthesisService
{
    public const string VoiceIdPrefix = PlatformSpeechVoiceIds.WindowsPrefix;

    public bool CanSynthesize(string voiceId) =>
        !string.IsNullOrWhiteSpace(voiceId)
        && voiceId.StartsWith(VoiceIdPrefix, StringComparison.Ordinal);

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SpeechVoice> voices = SpeechSynthesizer.AllVoices
            .Select(voice => new SpeechVoice(
                VoiceIdPrefix + voice.Id,
                voice.DisplayName,
                voice.Language,
                voice.Gender.ToString()))
            .ToArray();
        return Task.FromResult(voices);
    }

    public async Task<SpeechSynthesisResult> SynthesizeAsync(
        string text,
        string outputPath,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        string nativeVoiceId = options.VoiceId.StartsWith(
            VoiceIdPrefix,
            StringComparison.Ordinal)
            ? options.VoiceId[VoiceIdPrefix.Length..]
            : options.VoiceId;
        VoiceInformation voice = SpeechSynthesizer.AllVoices.FirstOrDefault(
                candidate => string.Equals(
                    candidate.Id,
                    nativeVoiceId,
                    StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The Windows speech voice '{options.VoiceId}' is not installed.");

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = outputPath + ".partial";
        try
        {
            using SpeechSynthesizer synthesizer = new();
            synthesizer.Voice = voice;
            synthesizer.Options.SpeakingRate = Math.Clamp(
                options.Speed,
                0.5,
                2.0);
            using SpeechSynthesisStream speechStream = await synthesizer
                .SynthesizeTextToStreamAsync(text);
            await using (Stream source = speechStream.AsStreamForRead())
            await using (FileStream destination = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous))
            {
                await source.CopyToAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            TimeSpan duration;
            int sampleRate;
            int channels;
            using (WaveFileReader reader = new(temporaryPath))
            {
                duration = reader.TotalTime;
                sampleRate = reader.WaveFormat.SampleRate;
                channels = reader.WaveFormat.Channels;
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            return new SpeechSynthesisResult(
                outputPath,
                duration,
                "windows.speech-synthesis.v1",
                options.VoiceId,
                sampleRate,
                channels);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
