using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Buddy.Core.Abstractions;
using Buddy.Speech;
using NAudio.Wave;

namespace Buddy.App.Platforms.MacCatalyst;

[SupportedOSPlatform("maccatalyst15.0")]
public sealed partial class MacOsSpeechSynthesisService :
    IPlatformSpeechSynthesisService
{
    private const string SayPath = "/usr/bin/say";
    private const string VoiceIdPrefix = PlatformSpeechVoiceIds.MacOsPrefix;
    private static readonly HashSet<string> SupportedLanguages = new(
        ["be", "de", "en", "es", "fr", "ru"],
        StringComparer.OrdinalIgnoreCase);

    public bool CanSynthesize(string voiceId) =>
        !string.IsNullOrWhiteSpace(voiceId)
        && voiceId.StartsWith(VoiceIdPrefix, StringComparison.Ordinal);

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SayPath))
        {
            return [];
        }

        ProcessResult result = await RunAsync(
                ["-v", "?"],
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        List<SpeechVoice> voices = [];
        foreach (string line in result.StandardOutput.Split('\n'))
        {
            Match match = VoiceLinePattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            string locale = match.Groups["locale"].Value.Replace('_', '-');
            string language = locale.Split('-', 2)[0];
            if (!SupportedLanguages.Contains(language))
            {
                continue;
            }

            string name = match.Groups["name"].Value.Trim();
            voices.Add(
                new SpeechVoice(
                    VoiceIdPrefix + name,
                    $"{name} · macOS",
                    locale,
                    "System"));
        }

        return voices
            .DistinctBy(voice => voice.Id, StringComparer.Ordinal)
            .OrderBy(voice => voice.Locale, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        if (!CanSynthesize(options.VoiceId))
        {
            throw new ArgumentException(
                "The selected voice does not belong to macOS speech.",
                nameof(options));
        }

        if (!File.Exists(SayPath))
        {
            throw new NotSupportedException(
                "The macOS say service is unavailable on this computer.");
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullOutputPath + ".partial";
        try
        {
            string nativeVoiceId = options.VoiceId[VoiceIdPrefix.Length..];
            int wordsPerMinute = checked((int)Math.Round(
                180 * Math.Clamp(options.Speed, 0.5f, 1.3f)));
            ProcessResult result = await RunAsync(
                    [
                        "-v", nativeVoiceId,
                        "-r", wordsPerMinute.ToString(CultureInfo.InvariantCulture),
                        "--file-format=WAVE",
                        "--data-format=LEI16@24000",
                        "-o", temporaryPath,
                        text,
                    ],
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? "macOS could not synthesize this text."
                        : result.StandardError.Trim());
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

            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    "macOS returned an empty speech file.");
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
            return new SpeechSynthesisResult(
                fullOutputPath,
                duration,
                "macos.say.v1",
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

    private static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(SayPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start macOS speech.");
        }

        try
        {
            Task<string> outputTask = process.StandardOutput
                .ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError
                .ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    [GeneratedRegex(
        @"^(?<name>.+?)\s+(?<locale>[a-z]{2}(?:_[A-Z]{2})?)\s+#",
        RegexOptions.CultureInvariant)]
    private static partial Regex VoiceLinePattern();

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
