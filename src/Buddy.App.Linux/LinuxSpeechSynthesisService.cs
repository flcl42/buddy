using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Buddy.Core.Abstractions;
using Buddy.Speech;
using NAudio.Wave;

namespace Buddy.App;

[SupportedOSPlatform("linux")]
public sealed partial class LinuxSpeechSynthesisService :
    IPlatformSpeechSynthesisService
{
    private const string VoiceIdPrefix = PlatformSpeechVoiceIds.LinuxPrefix;
    private static readonly string[] ExecutableCandidates =
    [
        "/usr/bin/espeak-ng",
        "/usr/local/bin/espeak-ng",
    ];
    private static readonly HashSet<string> SupportedLanguages = new(
        ["be", "de", "en", "es", "fr", "ru"],
        StringComparer.OrdinalIgnoreCase);

    public bool CanSynthesize(string voiceId) =>
        !string.IsNullOrWhiteSpace(voiceId)
        && voiceId.StartsWith(VoiceIdPrefix, StringComparison.Ordinal);

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        string? executable = FindExecutable();
        if (executable is null)
        {
            return [];
        }

        ProcessResult result = await RunAsync(
                executable,
                ["--voices"],
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

            string nativeId = match.Groups["locale"].Value;
            string displayName = match.Groups["name"].Value.Replace('_', ' ');
            string gender = match.Groups["gender"].Value.EndsWith(
                "/F",
                StringComparison.OrdinalIgnoreCase)
                ? "Female"
                : "Male";
            voices.Add(
                new SpeechVoice(
                    VoiceIdPrefix + nativeId,
                    $"{displayName} · eSpeak NG",
                    locale,
                    gender));
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
                "The selected voice does not belong to the Linux speech engine.",
                nameof(options));
        }

        string executable = FindExecutable()
            ?? throw new NotSupportedException(
                "Install espeak-ng to use the Linux system speaking voices.");
        string nativeVoiceId = options.VoiceId[VoiceIdPrefix.Length..];
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullOutputPath + ".partial.wav";
        try
        {
            int wordsPerMinute = checked((int)Math.Round(
                175 * Math.Clamp(options.Speed, 0.5f, 1.3f)));
            ProcessResult result = await RunAsync(
                    executable,
                    [
                        "--stdout",
                        "--voice", nativeVoiceId,
                        "--speed", wordsPerMinute.ToString(CultureInfo.InvariantCulture),
                        text,
                    ],
                    cancellationToken,
                    temporaryPath)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? "eSpeak NG could not synthesize this text."
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
                    "eSpeak NG returned an empty audio file.");
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
            return new SpeechSynthesisResult(
                fullOutputPath,
                duration,
                "linux.espeak-ng.v1",
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

    private static string? FindExecutable() => ExecutableCandidates
        .FirstOrDefault(File.Exists);

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? standardOutputPath = null)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start eSpeak NG.");
        }

        try
        {
            Task<string> errorTask = process.StandardError
                .ReadToEndAsync(cancellationToken);
            if (standardOutputPath is null)
            {
                Task<string> outputTask = process.StandardOutput
                    .ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new ProcessResult(
                    process.ExitCode,
                    await outputTask.ConfigureAwait(false),
                    await errorTask.ConfigureAwait(false));
            }

            await using (FileStream output = new(
                standardOutputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous))
            {
                await process.StandardOutput.BaseStream
                    .CopyToAsync(output, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessResult(
                process.ExitCode,
                string.Empty,
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
        @"^\s*\d+\s+(?<locale>[A-Za-z][A-Za-z0-9_-]*)\s+(?<gender>\S+)\s+(?<name>\S+)\s+(?<file>\S+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex VoiceLinePattern();

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
