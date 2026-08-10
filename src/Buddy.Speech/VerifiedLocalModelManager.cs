using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Buddy.Core.Abstractions;

namespace Buddy.Speech;

public sealed class VerifiedLocalModelManager : ILocalModelManager
{
    private const long DownloadHeadroomBytes = 128L * 1024 * 1024;
    private const int CopyBufferSize = 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, LocalModelManifest> DefaultModels =
        new Dictionary<string, LocalModelManifest>(StringComparer.Ordinal)
        {
            [LocalSpeechModels.KokoroEnglishV1] = new(
                LocalSpeechModels.KokoroEnglishV1,
                "Kokoro 82M English",
                "kokoro-v1.0-fp32.onnx",
                new Uri(
                    "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/"
                    + "download/v2.0.0/kokoro.onnx"),
                325_508_342,
                "0cfd5e79aab70a3d8c1a57dc639835110ddb32c9f5ff4fdd1f4db202ea43bb05"),
            [LocalSpeechModels.SileroVad] = new(
                LocalSpeechModels.SileroVad,
                "Silero VAD 6.2",
                "ggml-silero-v6.2.0.bin",
                new Uri(
                    "https://huggingface.co/sandrohanea/whisper.net/resolve/v4/vad/"
                    + "ggml-silero-v6.2.0.bin"),
                885_098,
                "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987"),
            [LocalSpeechModels.WhisperLargeV3Turbo] = new(
                LocalSpeechModels.WhisperLargeV3Turbo,
                "Whisper large-v3-turbo",
                "ggml-large-v3-turbo.bin",
                new Uri(
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/"
                    + "ggml-large-v3-turbo.bin"),
                1_624_555_275,
                "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69"),
        };

    private readonly string _modelsRoot;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, LocalModelManifest> _models;
    private readonly Dictionary<string, SemaphoreSlim> _installLocks;

    public VerifiedLocalModelManager(string modelsRoot, HttpClient httpClient)
        : this(modelsRoot, httpClient, DefaultModels)
    {
    }

    internal VerifiedLocalModelManager(
        string modelsRoot,
        HttpClient httpClient,
        IReadOnlyDictionary<string, LocalModelManifest> models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsRoot);
        _modelsRoot = Path.GetFullPath(modelsRoot);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _installLocks = models.Keys.ToDictionary(
            key => key,
            _ => new SemaphoreSlim(1, 1),
            StringComparer.Ordinal);

        foreach (LocalModelManifest manifest in models.Values)
        {
            ValidateManifest(manifest);
        }
    }

    public Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_modelsRoot);

        IReadOnlyList<LocalModelInfo> result = _models.Values
            .OrderBy(model => model.DownloadBytes)
            .Select(GetModelInfo)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task EnsureInstalledAsync(
        string modelId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (!_models.TryGetValue(modelId, out LocalModelManifest? manifest))
        {
            throw new KeyNotFoundException($"Unknown local model '{modelId}'.");
        }

        SemaphoreSlim installLock = _installLocks[modelId];
        await installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_modelsRoot);
            string finalPath = GetModelPath(manifest);
            if (IsVerified(finalPath, manifest))
            {
                progress?.Report(1);
                return;
            }

            if (File.Exists(finalPath)
                && new FileInfo(finalPath).Length == manifest.DownloadBytes)
            {
                string existingHash = await ComputeSha256Async(finalPath, cancellationToken)
                    .ConfigureAwait(false);
                if (string.Equals(
                        existingHash,
                        manifest.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await WriteVerificationStampAsync(
                            finalPath,
                            manifest,
                            cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report(1);
                    return;
                }
            }

            DeleteAppManagedFile(finalPath);
            DeleteAppManagedFile(GetStampPath(finalPath));
            await DownloadAndVerifyAsync(
                    manifest,
                    finalPath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            installLock.Release();
        }
    }

    private async Task DownloadAndVerifyAsync(
        LocalModelManifest manifest,
        string finalPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string partialPath = finalPath + ".partial";
        long partialLength = File.Exists(partialPath)
            ? new FileInfo(partialPath).Length
            : 0;
        if (partialLength > manifest.DownloadBytes)
        {
            DeleteAppManagedFile(partialPath);
            partialLength = 0;
        }

        EnsureFreeSpace(manifest.DownloadBytes - partialLength);

        using HttpRequestMessage request = new(HttpMethod.Get, manifest.DownloadUri);
        if (partialLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(partialLength, null);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        bool canResume = partialLength > 0
            && response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange?.From == partialLength;
        long writeOffset = canResume ? partialLength : 0;

        long completed = writeOffset;
        {
            await using Stream input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using FileStream output = new(
                partialPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            output.SetLength(writeOffset);
            output.Position = writeOffset;

            byte[] buffer = new byte[CopyBufferSize];
            progress?.Report(completed / (double)manifest.DownloadBytes);
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                completed = checked(completed + read);
                if (completed > manifest.DownloadBytes)
                {
                    throw new InvalidDataException(
                        $"The {manifest.DisplayName} download exceeded its pinned size.");
                }

                progress?.Report(completed / (double)manifest.DownloadBytes);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }

        if (completed != manifest.DownloadBytes)
        {
            throw new InvalidDataException(
                $"The {manifest.DisplayName} download is incomplete "
                + $"({completed:N0} of {manifest.DownloadBytes:N0} bytes).");
        }

        string actualHash = await ComputeSha256Async(partialPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                actualHash,
                manifest.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            DeleteAppManagedFile(partialPath);
            throw new InvalidDataException(
                $"The {manifest.DisplayName} download failed SHA-256 verification.");
        }

        File.Move(partialPath, finalPath, overwrite: false);
        await WriteVerificationStampAsync(finalPath, manifest, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(1);
    }

    private LocalModelInfo GetModelInfo(LocalModelManifest manifest)
    {
        string path = GetModelPath(manifest);
        LocalModelStatus status;
        if (!File.Exists(path))
        {
            status = File.Exists(path + ".partial")
                ? LocalModelStatus.Downloading
                : LocalModelStatus.NotInstalled;
        }
        else
        {
            status = IsVerified(path, manifest)
                ? LocalModelStatus.Ready
                : LocalModelStatus.Invalid;
        }

        return new LocalModelInfo(
            manifest.Id,
            manifest.DisplayName,
            manifest.DownloadBytes,
            manifest.Sha256,
            status,
            status is LocalModelStatus.Ready or LocalModelStatus.Invalid ? path : null);
    }

    private static bool IsVerified(string path, LocalModelManifest manifest)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo file = new(path);
        if (file.Length != manifest.DownloadBytes)
        {
            return false;
        }

        string stampPath = GetStampPath(path);
        if (!File.Exists(stampPath))
        {
            return false;
        }

        try
        {
            VerificationStamp? stamp = JsonSerializer.Deserialize<VerificationStamp>(
                File.ReadAllText(stampPath));
            return stamp is not null
                && string.Equals(stamp.Id, manifest.Id, StringComparison.Ordinal)
                && stamp.ByteLength == manifest.DownloadBytes
                && string.Equals(
                    stamp.Sha256,
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase)
                && stamp.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task WriteVerificationStampAsync(
        string modelPath,
        LocalModelManifest manifest,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(modelPath);
        VerificationStamp stamp = new(
            manifest.Id,
            manifest.DownloadBytes,
            manifest.Sha256,
            file.LastWriteTimeUtc.Ticks);
        string stampPath = GetStampPath(modelPath);
        string temporaryPath = stampPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(stamp),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, stampPath, overwrite: true);
        }
        finally
        {
            DeleteAppManagedFile(temporaryPath);
        }
    }

    private string GetModelPath(LocalModelManifest manifest)
    {
        string candidate = Path.GetFullPath(Path.Combine(_modelsRoot, manifest.FileName));
        string rootPrefix = _modelsRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A model path escaped the configured model root.");
        }

        return candidate;
    }

    private void EnsureFreeSpace(long remainingBytes)
    {
        string? root = Path.GetPathRoot(_modelsRoot);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        DriveInfo drive = new(root);
        long required = checked(Math.Max(remainingBytes, 0) + DownloadHeadroomBytes);
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Not enough free space to install the model. "
                + $"{required / (1024 * 1024):N0} MB is required.");
        }
    }

    private void DeleteAppManagedFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootPrefix = _modelsRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A model cleanup path escaped the model root.");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static string GetStampPath(string modelPath)
    {
        return modelPath + ".verified.json";
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void ValidateManifest(LocalModelManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.FileName);
        if (!string.Equals(
                manifest.FileName,
                Path.GetFileName(manifest.FileName),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Model filenames cannot include a directory.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(manifest.DownloadBytes, 0);
        if (manifest.Sha256.Length != 64
            || manifest.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Model SHA-256 values must contain 64 hexadecimal digits.");
        }
    }

    internal sealed record LocalModelManifest(
        string Id,
        string DisplayName,
        string FileName,
        Uri DownloadUri,
        long DownloadBytes,
        string Sha256);

    private sealed record VerificationStamp(
        string Id,
        long ByteLength,
        string Sha256,
        long LastWriteTimeUtcTicks);
}
