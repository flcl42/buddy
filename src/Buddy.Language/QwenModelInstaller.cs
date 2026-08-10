using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Buddy.Language;

public enum QwenInstallState
{
    Missing = 0,
    Installing = 1,
    Ready = 2,
}

public sealed record QwenInstallStatus(
    QwenInstallState State,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record QwenInstallProgress(
    double Fraction,
    string Stage,
    long CompletedBytes,
    long TotalBytes);

public interface IQwenModelInstaller
{
    Task<QwenInstallStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task EnsureInstalledAsync(
        IProgress<QwenInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class QwenModelInstaller : IQwenModelInstaller, IDisposable
{
    private const int BufferSize = 1024 * 1024;
    private const long DownloadHeadroomBytes = 512L * 1024 * 1024;
    private const string InstallMarkerFileName = ".buddy-qwen-install-v1.json";

    private static readonly QwenAsset TargetModel = new(
        "Qwen 3.6 27B Q4_K_M",
        new Uri(
            "https://huggingface.co/ggml-org/Qwen3.6-27B-GGUF/resolve/"
            + "4c8d89a3b10d66695ded02bacee44f9dcf64848b/"
            + "Qwen3.6-27B-Q4_K_M.gguf"),
        19_095_766_304,
        "65b753ea835627f7b511143c6ceb976525c7f21f5df8c664bc0a9c23d1c49921");

    private static readonly QwenAsset DraftModel = new(
        "Qwen DFlash Q8_0 draft",
        new Uri(
            "https://huggingface.co/ggml-org/Qwen3.6-27B-GGUF/resolve/"
            + "4c8d89a3b10d66695ded02bacee44f9dcf64848b/"
            + "dflash-Qwen3.6-27B-Q8_0.gguf"),
        1_849_481_440,
        "a31adddb37adaca315b94a18d96d124135ee15b76b7249986e77057267b01909");

    private static readonly QwenAsset LlamaRuntime = new(
        "llama.cpp b10243 CUDA runtime",
        new Uri(
            "https://github.com/ggml-org/llama.cpp/releases/download/b10243/"
            + "llama-b10243-bin-win-cuda-13.3-x64.zip"),
        146_530_911,
        "9faa877cdc6618ecbb2c8354809ebc1db3b5c24305559dda73340bf23f1a3fd4");

    private static readonly QwenAsset CudaRuntime = new(
        "CUDA 13.3 libraries",
        new Uri(
            "https://github.com/ggml-org/llama.cpp/releases/download/b10243/"
            + "cudart-llama-bin-win-cuda-13.3-x64.zip"),
        390_970_417,
        "1462a050eb4c684921ba51dcc4cc488a036674c3e73e9945ee705b854808d03e");

    private readonly HttpClient _httpClient;
    private readonly QwenRuntimeOptions _options;
    private readonly string _root;
    private readonly string _downloads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _installing;

    public QwenModelInstaller(HttpClient httpClient, QwenRuntimeOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        string runtimeParent = Directory.GetParent(_options.RuntimeDirectory)?.FullName
            ?? throw new InvalidOperationException("The Qwen runtime path has no parent.");
        _root = Directory.GetParent(runtimeParent)?.FullName
            ?? throw new InvalidOperationException("The Qwen installation path has no root.");
        _root = Path.GetFullPath(_root);
        EnsureInsideRoot(_options.RuntimeDirectory);
        EnsureInsideRoot(_options.ModelPath);
        if (_options.DraftModelPath is not null)
        {
            EnsureInsideRoot(_options.DraftModelPath);
        }

        _downloads = Path.Combine(_root, "downloads");
    }

    public Task<QwenInstallStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_installing)
        {
            return Task.FromResult(
                new QwenInstallStatus(
                    QwenInstallState.Installing,
                    "Installing and verifying Qwen local AI…",
                    DateTimeOffset.Now));
        }

        bool ready = HasInstalledFiles() && HasCurrentMarker();
        return Task.FromResult(
            new QwenInstallStatus(
                ready ? QwenInstallState.Ready : QwenInstallState.Missing,
                ready
                    ? "Installed and SHA-256 verified · 21.5 GB models · pinned CUDA runtime"
                    : "Not installed · activates a resumable 21.5 GB verified download",
                DateTimeOffset.Now));
    }

    public async Task EnsureInstalledAsync(
        IProgress<QwenInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _installing = true;
        try
        {
            if (HasInstalledFiles() && HasCurrentMarker())
            {
                Report(progress, 1, "Qwen is ready", TotalDownloadBytes, TotalDownloadBytes);
                return;
            }

            Directory.CreateDirectory(_downloads);
            Directory.CreateDirectory(
                Path.GetDirectoryName(_options.ModelPath)
                    ?? throw new InvalidOperationException("The Qwen model directory is invalid."));
            EnsureFreeSpace();
            long completed = 0;
            await EnsureAssetAsync(
                    TargetModel,
                    _options.ModelPath,
                    completed,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            completed += TargetModel.Bytes;

            if (_options.DraftModelPath is not null)
            {
                await EnsureAssetAsync(
                        DraftModel,
                        _options.DraftModelPath,
                        completed,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            completed += DraftModel.Bytes;
            string llamaArchive = Path.Combine(
                _downloads,
                "llama-b10243-bin-win-cuda-13.3-x64.zip");
            await EnsureAssetAsync(
                    LlamaRuntime,
                    llamaArchive,
                    completed,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            completed += LlamaRuntime.Bytes;
            string cudaArchive = Path.Combine(
                _downloads,
                "cudart-llama-bin-win-cuda-13.3-x64.zip");
            await EnsureAssetAsync(
                    CudaRuntime,
                    cudaArchive,
                    completed,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            completed += CudaRuntime.Bytes;

            Report(progress, 0.999, "Installing the verified CUDA runtime", completed, TotalDownloadBytes);
            await InstallRuntimeAsync(
                    llamaArchive,
                    cudaArchive,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteInstallMarkerAsync(cancellationToken).ConfigureAwait(false);
            Report(progress, 1, "Qwen local AI is ready", TotalDownloadBytes, TotalDownloadBytes);
        }
        finally
        {
            _installing = false;
            _gate.Release();
        }
    }

    private static long TotalDownloadBytes => TargetModel.Bytes
        + DraftModel.Bytes
        + LlamaRuntime.Bytes
        + CudaRuntime.Bytes;

    private async Task EnsureAssetAsync(
        QwenAsset asset,
        string finalPath,
        long completedBefore,
        IProgress<QwenInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string stampPath = finalPath + ".verified.json";
        if (File.Exists(finalPath)
            && new FileInfo(finalPath).Length == asset.Bytes
            && IsStampCurrent(stampPath, asset, finalPath))
        {
            Report(
                progress,
                (completedBefore + asset.Bytes) / (double)TotalDownloadBytes,
                $"Verified {asset.Name}",
                completedBefore + asset.Bytes,
                TotalDownloadBytes);
            return;
        }

        if (File.Exists(finalPath) && new FileInfo(finalPath).Length == asset.Bytes)
        {
            Report(
                progress,
                completedBefore / (double)TotalDownloadBytes,
                $"Verifying {asset.Name}",
                completedBefore,
                TotalDownloadBytes);
            string existingHash = await ComputeSha256Async(finalPath, cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(existingHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await WriteStampAsync(stampPath, asset, finalPath, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        DeleteFileInsideRoot(finalPath);
        DeleteFileInsideRoot(stampPath);
        string partialPath = finalPath + ".partial";
        long partialBytes = File.Exists(partialPath)
            ? new FileInfo(partialPath).Length
            : 0;
        if (partialBytes > asset.Bytes)
        {
            DeleteFileInsideRoot(partialPath);
            partialBytes = 0;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, asset.Uri);
        if (partialBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(partialBytes, null);
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        bool resumed = partialBytes > 0
            && response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange?.From == partialBytes;
        long written = resumed ? partialBytes : 0;
        await using Stream input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (FileStream output = new(
            partialPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            output.SetLength(written);
            output.Position = written;
            byte[] buffer = new byte[BufferSize];
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                written = checked(written + read);
                if (written > asset.Bytes)
                {
                    throw new InvalidDataException(
                        $"The {asset.Name} download exceeded its pinned size.");
                }

                long aggregate = completedBefore + written;
                Report(
                    progress,
                    aggregate / (double)TotalDownloadBytes,
                    $"Downloading {asset.Name}",
                    aggregate,
                    TotalDownloadBytes);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }

        if (written != asset.Bytes)
        {
            throw new InvalidDataException(
                $"The {asset.Name} download is incomplete ({written:N0} of {asset.Bytes:N0} bytes).");
        }

        Report(
            progress,
            (completedBefore + written) / (double)TotalDownloadBytes,
            $"Verifying {asset.Name}",
            completedBefore + written,
            TotalDownloadBytes);
        string hash = await ComputeSha256Async(partialPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteFileInsideRoot(partialPath);
            throw new InvalidDataException(
                $"The {asset.Name} download failed SHA-256 verification.");
        }

        File.Move(partialPath, finalPath, overwrite: true);
        await WriteStampAsync(stampPath, asset, finalPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InstallRuntimeAsync(
        string llamaArchive,
        string cudaArchive,
        CancellationToken cancellationToken)
    {
        string runtimeParent = Directory.GetParent(_options.RuntimeDirectory)?.FullName
            ?? throw new InvalidOperationException("The runtime directory has no parent.");
        Directory.CreateDirectory(runtimeParent);
        string staging = Path.Combine(
            runtimeParent,
            $".b10243-install-{Guid.NewGuid():N}");
        EnsureInsideRoot(staging);
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractArchiveAsync(llamaArchive, staging, cancellationToken)
                .ConfigureAwait(false);
            await ExtractArchiveAsync(cudaArchive, staging, cancellationToken)
                .ConfigureAwait(false);
            string server = Path.Combine(staging, QwenRuntimeOptions.ServerFileName);
            if (!File.Exists(server)
                || !File.Exists(Path.Combine(staging, "ggml-cuda.dll"))
                || !File.Exists(Path.Combine(staging, "cudart64_13.dll")))
            {
                throw new InvalidDataException(
                    "The verified llama.cpp archives did not contain the required runtime files.");
            }

            if (Directory.Exists(_options.RuntimeDirectory))
            {
                string backup = _options.RuntimeDirectory
                    + $".replaced-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                EnsureInsideRoot(backup);
                Directory.Move(_options.RuntimeDirectory, backup);
            }

            Directory.Move(staging, _options.RuntimeDirectory);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream file = new(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using ZipArchive archive = new(file, ZipArchiveMode.Read, leaveOpen: false);
        string destinationPrefix = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!outputPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A runtime archive entry escaped its staging directory.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)
                    ?? throw new InvalidOperationException("A runtime archive path is invalid."));
            await using Stream input = entry.Open();
            await using FileStream output = new(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool HasInstalledFiles()
    {
        return File.Exists(_options.ServerPath)
            && File.Exists(_options.ModelPath)
            && new FileInfo(_options.ModelPath).Length == TargetModel.Bytes
            && (_options.DraftModelPath is null
                || (File.Exists(_options.DraftModelPath)
                    && new FileInfo(_options.DraftModelPath).Length == DraftModel.Bytes));
    }

    private bool HasCurrentMarker()
    {
        string markerPath = Path.Combine(_options.RuntimeDirectory, InstallMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            InstallMarker? marker = JsonSerializer.Deserialize<InstallMarker>(
                File.ReadAllText(markerPath));
            return marker is not null
                && marker.Version == 1
                && string.Equals(marker.TargetSha256, TargetModel.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(marker.DraftSha256, DraftModel.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(marker.RuntimeSha256, LlamaRuntime.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(marker.CudaSha256, CudaRuntime.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return false;
        }
    }

    private async Task WriteInstallMarkerAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_options.RuntimeDirectory, InstallMarkerFileName);
        InstallMarker marker = new(
            1,
            TargetModel.Sha256,
            DraftModel.Sha256,
            LlamaRuntime.Sha256,
            CudaRuntime.Sha256,
            DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(marker),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsStampCurrent(
        string stampPath,
        QwenAsset asset,
        string filePath)
    {
        if (!File.Exists(stampPath))
        {
            return false;
        }

        try
        {
            AssetStamp? stamp = JsonSerializer.Deserialize<AssetStamp>(
                File.ReadAllText(stampPath));
            FileInfo file = new(filePath);
            return stamp is not null
                && stamp.Bytes == asset.Bytes
                && stamp.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks
                && string.Equals(stamp.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return false;
        }
    }

    private async Task WriteStampAsync(
        string stampPath,
        QwenAsset asset,
        string filePath,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(filePath);
        string temporary = stampPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(
                        new AssetStamp(
                            asset.Bytes,
                            asset.Sha256,
                            file.LastWriteTimeUtc.Ticks)),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, stampPath, overwrite: true);
        }
        finally
        {
            DeleteFileInsideRoot(temporary);
        }
    }

    private void EnsureFreeSpace()
    {
        string? root = Path.GetPathRoot(_root);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        long remaining = 0;
        remaining += RemainingBytes(_options.ModelPath, TargetModel.Bytes);
        if (_options.DraftModelPath is not null)
        {
            remaining += RemainingBytes(_options.DraftModelPath, DraftModel.Bytes);
        }

        remaining += RemainingBytes(
            Path.Combine(
                _downloads,
                "llama-b10243-bin-win-cuda-13.3-x64.zip"),
            LlamaRuntime.Bytes);
        remaining += RemainingBytes(
            Path.Combine(
                _downloads,
                "cudart-llama-bin-win-cuda-13.3-x64.zip"),
            CudaRuntime.Bytes);
        long required = checked(remaining + DownloadHeadroomBytes);
        if (new DriveInfo(root).AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Qwen needs {required / (1024d * 1024 * 1024):N1} GB of free space to finish installation.");
        }
    }

    private static long RemainingBytes(string finalPath, long total)
    {
        string partial = finalPath + ".partial";
        long present = File.Exists(finalPath)
            ? Math.Min(new FileInfo(finalPath).Length, total)
            : File.Exists(partial)
                ? Math.Min(new FileInfo(partial).Length, total)
                : 0;
        return total - present;
    }

    private void EnsureInsideRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string prefix = _root.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A Qwen installation path escaped its configured root.");
        }
    }

    private void DeleteFileInsideRoot(string path)
    {
        EnsureInsideRoot(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void Report(
        IProgress<QwenInstallProgress>? progress,
        double fraction,
        string stage,
        long completed,
        long total)
    {
        progress?.Report(
            new QwenInstallProgress(
                Math.Clamp(fraction, 0, 1),
                stage,
                Math.Clamp(completed, 0, total),
                total));
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record QwenAsset(string Name, Uri Uri, long Bytes, string Sha256);

    private sealed record AssetStamp(long Bytes, string Sha256, long LastWriteTimeUtcTicks);

    private sealed record InstallMarker(
        int Version,
        string TargetSha256,
        string DraftSha256,
        string RuntimeSha256,
        string CudaSha256,
        DateTimeOffset InstalledUtc);
}
