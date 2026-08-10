using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Buddy.Core.Abstractions;
using Buddy.Core.Services;
using Buddy.Persistence;
using Buddy.Speech;

namespace Buddy.App.Services;

public sealed class DialogSpeechCacheService : IDisposable
{
    private const string PreferredVoiceId = "af_heart";
    private const float SpeechSpeed = 1.0f;
    private const long MaximumCacheBytes = 512L * 1024 * 1024;
    private const long TrimmedCacheBytes = 448L * 1024 * 1024;

    private readonly ISpeechSynthesisService _synthesis;
    private readonly BuddyDataPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public DialogSpeechCacheService(
        ISpeechSynthesisService synthesis,
        BuddyDataPaths paths)
    {
        _synthesis = synthesis ?? throw new ArgumentNullException(nameof(synthesis));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<string> GetOrCreateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string speechText = MarkdownTextProcessor.ToSpeechText(text);
        if (string.IsNullOrWhiteSpace(speechText))
        {
            throw new ArgumentException(
                "The selected text contains no speakable words.",
                nameof(text));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IReadOnlyList<SpeechVoice> voices = await _synthesis
                .GetVoicesAsync(cancellationToken)
                .ConfigureAwait(false);
            SpeechVoice? voice = voices.FirstOrDefault(
                    item => item.Id == PreferredVoiceId)
                ?? (voices.Count > 0 ? voices[0] : null);
            if (voice is null)
            {
                throw new InvalidOperationException(
                    "No local speaking voice is available.");
            }

            string cachePath = GetCachePath(speechText, voice.Id);
            FileInfo cached = new(cachePath);
            if (cached.Exists && cached.Length > 44)
            {
                TryTouch(cached);
                return cachePath;
            }

            Directory.CreateDirectory(_paths.SpeechCache);
            SpeechSynthesisResult result = await _synthesis
                .SynthesizeAsync(
                    speechText,
                    cachePath,
                    new SpeechSynthesisOptions(voice.Id, SpeechSpeed, []),
                    cancellationToken)
                .ConfigureAwait(false);
            TrimCache(result.OutputPath);
            return result.OutputPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private string GetCachePath(string speechText, string voiceId)
    {
        string cacheIdentity = string.Join(
            '\n',
            KokoroSpeechSynthesisService.SynthesisVersion,
            MarkdownTextProcessor.SpeechNormalizationVersion,
            voiceId,
            SpeechSpeed.ToString("R", CultureInfo.InvariantCulture),
            speechText);
        string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(cacheIdentity)))
            .ToLowerInvariant();
        return Path.Combine(_paths.SpeechCache, $"speech-{hash}.wav");
    }

    private void TrimCache(string currentPath)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(_paths.SpeechCache)
                .EnumerateFiles("speech-*.wav", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.LastAccessTimeUtc)
                .ThenBy(file => file.LastWriteTimeUtc)
                .ToArray();
            long totalBytes = files.Sum(file => file.Length);
            if (totalBytes <= MaximumCacheBytes)
            {
                return;
            }

            foreach (FileInfo file in files)
            {
                if (totalBytes <= TrimmedCacheBytes)
                {
                    break;
                }

                if (string.Equals(
                    file.FullName,
                    currentPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long length = file.Length;
                try
                {
                    file.Delete();
                    totalBytes -= length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryTouch(FileInfo file)
    {
        try
        {
            file.LastAccessTimeUtc = DateTime.UtcNow;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
