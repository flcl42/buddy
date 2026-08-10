using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Language;

public enum QwenRuntimeState
{
    Missing = 0,
    ReadyOnDisk = 1,
    Loading = 2,
    Loaded = 3,
    Failed = 4,
}

public sealed record QwenRuntimeStatus(
    QwenRuntimeState State,
    string Message,
    DateTimeOffset CheckedAt);

public interface IQwenModelRuntime : IAsyncDisposable
{
    QwenRuntimeOptions Options { get; }

    string ApiKey { get; }

    Task<QwenRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    Task<int> CountTokensAsync(
        string content,
        CancellationToken cancellationToken = default);

    Task UnloadAsync(CancellationToken cancellationToken = default);
}

public sealed partial class QwenModelRuntime : IQwenModelRuntime
{
    public const long ExpectedModelBytes = 19_095_766_304;
    public const long ExpectedDraftModelBytes = 1_849_481_440;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _messageGate = new();
    private readonly Queue<string> _recentMessages = new();
    private Process? _process;
    private SafeFileHandle? _killOnCloseJob;
    private bool _disposed;
    private bool _loading;

    public QwenModelRuntime(HttpClient httpClient, QwenRuntimeOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public QwenRuntimeOptions Options { get; }

    public string ApiKey { get; }

    public async Task<QwenRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? installationProblem = GetInstallationProblem();
        if (installationProblem is not null)
        {
            return new QwenRuntimeStatus(
                QwenRuntimeState.Missing,
                installationProblem,
                DateTimeOffset.Now);
        }

        if (await IsServerReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            string acceleration = Options.IsDFlashEnabled
                ? " · DFlash accelerated"
                : string.Empty;
            return new QwenRuntimeStatus(
                QwenRuntimeState.Loaded,
                $"Loaded locally · {Options.ContextSize:N0} token context · {Options.GpuLayers} GPU layers{acceleration}",
                DateTimeOffset.Now);
        }

        if (_loading || (_process is { HasExited: false }))
        {
            return new QwenRuntimeStatus(
                QwenRuntimeState.Loading,
                "Loading Qwen into memory…",
                DateTimeOffset.Now);
        }

        if (_process is { HasExited: true } process)
        {
            return new QwenRuntimeStatus(
                QwenRuntimeState.Failed,
                BuildFailureMessage(process.ExitCode),
                DateTimeOffset.Now);
        }

        return new QwenRuntimeStatus(
            QwenRuntimeState.ReadyOnDisk,
            "Ready on disk · loads automatically with the first AI request",
            DateTimeOffset.Now);
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? installationProblem = GetInstallationProblem();
        if (installationProblem is not null)
        {
            throw new InvalidOperationException(installationProblem);
        }

        if (await IsServerReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (await IsServerReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (_process is null || _process.HasExited)
            {
                DisposeProcess();
                StartServer();
            }

            _loading = true;
            await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loading = false;
            _lifecycleGate.Release();
        }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            DisposeProcess();
            _killOnCloseJob?.Dispose();
            _killOnCloseJob = null;
            ClearRecentMessages();
        }
        finally
        {
            _loading = false;
            _lifecycleGate.Release();
        }
    }

    public async Task<int> CountTokensAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri(Options.BaseAddress, "tokenize"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { content }),
            Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Qwen token counting returned HTTP {(int)response.StatusCode}.");
        }

        string json = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("tokens", out JsonElement tokens)
            || tokens.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Qwen token counting returned an invalid response.");
        }

        return tokens.GetArrayLength();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            DisposeProcess();
            _killOnCloseJob?.Dispose();
            _killOnCloseJob = null;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private string? GetInstallationProblem()
    {
        if (!File.Exists(Options.ServerPath))
        {
            return $"llama.cpp runtime is missing · {Options.ServerPath}";
        }

        if (!File.Exists(Options.ModelPath))
        {
            return $"Qwen 3.6 27B model is missing · {Options.ModelPath}";
        }

        long modelBytes = new FileInfo(Options.ModelPath).Length;
        if (modelBytes != ExpectedModelBytes)
        {
            return $"Qwen model is incomplete ({modelBytes:N0} of {ExpectedModelBytes:N0} bytes).";
        }

        if (Options.IsDFlashEnabled)
        {
            if (!File.Exists(Options.DraftModelPath))
            {
                return $"Qwen DFlash draft model is missing · {Options.DraftModelPath}";
            }

            long draftModelBytes = new FileInfo(Options.DraftModelPath!).Length;
            if (draftModelBytes != ExpectedDraftModelBytes)
            {
                return $"Qwen DFlash draft model is incomplete ({draftModelBytes:N0} of {ExpectedDraftModelBytes:N0} bytes).";
            }
        }

        return null;
    }

    private void StartServer()
    {
        Directory.CreateDirectory(Options.LogDirectory);
        ClearRecentMessages();
        ProcessStartInfo startInfo = new()
        {
            FileName = Options.ServerPath,
            WorkingDirectory = Options.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        AddArguments(startInfo.ArgumentList);

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += OnServerOutput;
        process.ErrorDataReceived += OnServerOutput;
        process.Exited += OnServerExited;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("llama.cpp did not start.");
        }

        try
        {
            AssignToKillOnCloseJob(process);
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
            throw;
        }
    }

    private void AddArguments(System.Collections.ObjectModel.Collection<string> arguments)
    {
        arguments.Add("--model");
        arguments.Add(Options.ModelPath);
        arguments.Add("--alias");
        arguments.Add(QwenLanguageProvider.ModelAlias);
        arguments.Add("--host");
        arguments.Add("127.0.0.1");
        arguments.Add("--port");
        arguments.Add(Options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("--ctx-size");
        arguments.Add(Options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("--parallel");
        arguments.Add("1");
        arguments.Add("--n-gpu-layers");
        arguments.Add(Options.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Options.IsDFlashEnabled)
        {
            arguments.Add("--model-draft");
            arguments.Add(Options.DraftModelPath!);
            arguments.Add("--spec-type");
            arguments.Add("draft-dflash");
            arguments.Add("--spec-draft-n-max");
            arguments.Add(Options.SpeculativeDraftTokens.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            arguments.Add("--n-gpu-layers-draft");
            arguments.Add(Options.DraftGpuLayers.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            arguments.Add("--spec-draft-threads");
            arguments.Add(Options.CpuThreads.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            arguments.Add("--spec-draft-threads-batch");
            arguments.Add(Options.CpuThreads.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            arguments.Add("--spec-draft-type-k");
            arguments.Add("q8_0");
            arguments.Add("--spec-draft-type-v");
            arguments.Add("q8_0");
        }
        arguments.Add("--threads");
        arguments.Add(Options.CpuThreads.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("--threads-batch");
        arguments.Add(Options.CpuThreads.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("--flash-attn");
        arguments.Add("on");
        arguments.Add("--cache-type-k");
        arguments.Add("q8_0");
        arguments.Add("--cache-type-v");
        arguments.Add("q8_0");
        arguments.Add("--reasoning");
        arguments.Add("off");
        arguments.Add("--jinja");
        arguments.Add("--no-webui");
        arguments.Add("--no-context-shift");
        arguments.Add("--api-key");
        arguments.Add(ApiKey);
        if (Options.SleepIdleSeconds >= 0)
        {
            arguments.Add("--sleep-idle-seconds");
            arguments.Add(Options.SleepIdleSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Options.EffectiveStartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process? process = _process;
            if (process is null || process.HasExited)
            {
                int? exitCode = process?.ExitCode;
                throw new InvalidOperationException(
                    BuildFailureMessage(exitCode));
            }

            if (await IsServerReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Qwen did not finish loading within {Options.EffectiveStartupTimeout.TotalMinutes:N0} minutes. "
            + GetRecentMessageSummary());
    }

    private async Task<bool> IsServerReadyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri(Options.BaseAddress, "health"));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", ApiKey);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private void OnServerOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        string line = $"{DateTimeOffset.Now:O} {eventArgs.Data}";
        lock (_messageGate)
        {
            _recentMessages.Enqueue(line);
            while (_recentMessages.Count > 40)
            {
                _recentMessages.Dequeue();
            }
        }

        try
        {
            File.AppendAllText(
                Path.Combine(Options.LogDirectory, "qwen-server.log"),
                line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Runtime diagnostics must never crash the application.
        }
        catch (UnauthorizedAccessException)
        {
            // Runtime diagnostics must never crash the application.
        }
    }

    private void OnServerExited(object? sender, EventArgs eventArgs)
    {
        if (sender is Process process)
        {
            AddRecentMessage($"llama.cpp exited with code {process.ExitCode}.");
        }
    }

    private string BuildFailureMessage(int? exitCode)
    {
        string exit = exitCode.HasValue ? $" (exit code {exitCode.Value})" : string.Empty;
        return $"Qwen could not be loaded{exit}. {GetRecentMessageSummary()}";
    }

    private string GetRecentMessageSummary()
    {
        lock (_messageGate)
        {
            string? message = _recentMessages.LastOrDefault(
                line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("failed", StringComparison.OrdinalIgnoreCase));
            message ??= _recentMessages.LastOrDefault();
            return string.IsNullOrWhiteSpace(message)
                ? $"See {Path.Combine(Options.LogDirectory, "qwen-server.log")}."
                : message;
        }
    }

    private void AddRecentMessage(string message)
    {
        lock (_messageGate)
        {
            _recentMessages.Enqueue(message);
            while (_recentMessages.Count > 40)
            {
                _recentMessages.Dequeue();
            }
        }
    }

    private void ClearRecentMessages()
    {
        lock (_messageGate)
        {
            _recentMessages.Clear();
        }
    }

    private void DisposeProcess()
    {
        if (_process is null)
        {
            return;
        }

        _process.OutputDataReceived -= OnServerOutput;
        _process.ErrorDataReceived -= OnServerOutput;
        _process.Exited -= OnServerExited;
        _process.Dispose();
        _process = null;
    }

    private void AssignToKillOnCloseJob(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_killOnCloseJob is null || _killOnCloseJob.IsInvalid)
        {
            SafeFileHandle job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not create the Qwen lifecycle job.");
            }

            JobObjectExtendedLimitInformation information = new();
            information.BasicLimitInformation.LimitFlags =
                JobObjectLimitKillOnJobClose;
            int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            if (SetInformationJobObject(
                    job,
                    JobObjectInformationClass.ExtendedLimitInformation,
                    ref information,
                    (uint)size) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(
                    error,
                    "Could not configure the Qwen lifecycle job.");
            }

            _killOnCloseJob = job;
        }

        if (AssignProcessToJobObject(_killOnCloseJob, process.Handle) == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not attach llama.cpp to Buddy's lifecycle.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private const uint JobObjectLimitKillOnJobClose = 0x0000_2000;

    private enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateJobObjectW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateJobObject(
        nint jobAttributes,
        string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetInformationJobObject(
        SafeFileHandle job,
        JobObjectInformationClass informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AssignProcessToJobObject(
        SafeFileHandle job,
        nint process);
}
