using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Core.Services;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;
using NAudio.Wave;

namespace Buddy.Speech;

public sealed class KokoroSpeechSynthesisService :
    ISpeechSynthesisService,
    IDisposable
{
    public const string SynthesisVersion = "buddy.kokoro-safe-sequences.v1";

    private const int MaximumInputCharacters = 12_000;
    private const int KokoroSampleRate = 24_000;
    private const int InterChunkSilenceMilliseconds = 80;

    private static readonly IReadOnlyList<SpeechVoice> Voices =
    [
        new("af_heart", "Heart · warm female", "en-US", "Female"),
        new("am_michael", "Michael · clear male", "en-US", "Male"),
        new("bf_emma", "Emma · warm British female", "en-GB", "Female"),
        new("bm_george", "George · clear British male", "en-GB", "Male"),
    ];

    private readonly ILocalModelManager _models;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private KokoroWavSynthesizer? _synthesizer;
    private string? _loadedModelPath;
    private bool _disposed;

    public KokoroSpeechSynthesisService(ILocalModelManager models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Voices);
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
        if (text.Length > MaximumInputCharacters)
        {
            throw new ArgumentException(
                $"Speech text cannot exceed {MaximumInputCharacters:N0} characters.",
                nameof(text));
        }

        string speechText = MarkdownTextProcessor.ToSpeechText(text.Trim());
        if (string.IsNullOrWhiteSpace(speechText))
        {
            throw new ArgumentException(
                "Speech text contains no readable content.",
                nameof(text));
        }

        SpeechVoice selectedVoice = Voices.FirstOrDefault(
                voice => string.Equals(
                    voice.Id,
                    options.VoiceId,
                    StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"Unknown Kokoro voice '{options.VoiceId}'.",
                nameof(options));
        if (options.Speed is < 0.5f or > 1.3f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Kokoro speech speed must be between 0.5 and 1.3.");
        }

        string destinationPath = Path.GetFullPath(outputPath);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException(
                "Speech output must have a parent directory.",
                nameof(outputPath));
        }

        await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string modelPath = await GetVerifiedModelPathAsync(cancellationToken)
                .ConfigureAwait(false);
            string runtimeRoot = KokoroRuntimeAssets.GetRootPath();
            KokoroWavSynthesizer synthesizer = GetOrCreateSynthesizer(modelPath);
            KokoroVoice voice = GetVoice(selectedVoice.Id, runtimeRoot);
            KokoroTTSPipelineConfig pipeline = new()
            {
                Speed = options.Speed,
                PreprocessText = true,
            };

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> chunks = KokoroSynthesisTextChunker.Split(
                speechText);
            List<byte[]> audioChunks = new(chunks.Count);
            await KokoroTokenizerGate.Instance.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            bool previousNativeEnglish = Tokenizer.UseNativeEnglish;
            try
            {
                Tokenizer.eSpeakNGPath = Path.Combine(runtimeRoot, "espeak");
                Tokenizer.UseNativeEnglish = true;
                foreach (string chunk in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string preparedText = ApplyGlossary(
                        chunk,
                        options.Glossary);
                    preparedText = KokoroSynthesisTextPreprocessor
                        .ApplyEnglishContractionSpeechForms(preparedText);
                    byte[] chunkAudio = await synthesizer
                        .SynthesizeAsync(preparedText, voice, pipeline)
                        .ConfigureAwait(false);
                    if (chunkAudio.Length == 0)
                    {
                        throw new InvalidDataException(
                            "Kokoro returned an empty audio chunk.");
                    }

                    if (chunkAudio.Length % sizeof(short) != 0)
                    {
                        throw new InvalidDataException(
                            "Kokoro returned an incomplete PCM sample.");
                    }

                    audioChunks.Add(chunkAudio);
                }
            }
            finally
            {
                Tokenizer.UseNativeEnglish = previousNativeEnglish;
                KokoroTokenizerGate.Instance.Release();
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] audioBytes = JoinAudioChunks(audioChunks);

            Directory.CreateDirectory(destinationDirectory);
            string temporaryPath = destinationPath
                + $".buddy-{Guid.NewGuid():N}.tmp.wav";
            try
            {
                await WriteWaveAsync(
                        temporaryPath,
                        audioBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                TimeSpan duration = ValidateWave(temporaryPath);
                File.Move(temporaryPath, destinationPath, overwrite: true);
                return new SpeechSynthesisResult(
                    destinationPath,
                    duration,
                    LocalSpeechModels.KokoroEnglishV1,
                    selectedVoice.Id);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _synthesizer?.Dispose();
        _inferenceGate.Dispose();
    }

    private async Task<string> GetVerifiedModelPathAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalModelInfo? model = models.FirstOrDefault(
            item => item.Id == LocalSpeechModels.KokoroEnglishV1);
        if (model?.Status != LocalModelStatus.Ready
            || string.IsNullOrWhiteSpace(model.LocalPath))
        {
            throw new LocalModelNotInstalledException(
                LocalSpeechModels.KokoroEnglishV1,
                "Kokoro English");
        }

        return model.LocalPath;
    }

    private KokoroWavSynthesizer GetOrCreateSynthesizer(string modelPath)
    {
        if (_synthesizer is not null
            && string.Equals(
                _loadedModelPath,
                modelPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return _synthesizer;
        }

        _synthesizer?.Dispose();
        _synthesizer = new KokoroWavSynthesizer(modelPath);
        _loadedModelPath = modelPath;
        return _synthesizer;
    }

    private static KokoroVoice GetVoice(string voiceId, string runtimeRoot)
    {
        string voicesPath = Path.Combine(runtimeRoot, "voices");

        KokoroVoiceManager.LoadVoicesFromPath(voicesPath);
        try
        {
            return KokoroVoiceManager.GetVoice(voiceId);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidDataException(
                $"The bundled Kokoro voice '{voiceId}' could not be loaded.",
                error);
        }
    }

    private static string ApplyGlossary(
        string text,
        IReadOnlyList<GlossaryTerm>? glossary)
    {
        if (glossary is null || glossary.Count == 0)
        {
            return text;
        }

        string result = text;
        foreach (GlossaryTerm term in glossary)
        {
            if (string.IsNullOrWhiteSpace(term.WrittenForm)
                || string.IsNullOrWhiteSpace(term.Pronunciation)
                || term.Pronunciation.IndexOfAny(['/', '[', ']', '(', ')']) >= 0)
            {
                continue;
            }

            string literal = $"[{term.WrittenForm}](/"
                + $"{term.Pronunciation.Trim()}/)";
            result = result.Replace(
                term.WrittenForm,
                literal,
                StringComparison.Ordinal);
        }

        return result;
    }

    private static async Task WriteWaveAsync(
        string path,
        byte[] audioBytes,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using WaveFileWriter writer = new(
            stream,
            new WaveFormat(KokoroSampleRate, 16, 1));
        await writer.WriteAsync(audioBytes, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] JoinAudioChunks(List<byte[]> chunks)
    {
        if (chunks.Count == 0)
        {
            throw new InvalidDataException("Kokoro returned no audio chunks.");
        }

        int silenceByteCount = KokoroSampleRate
            * InterChunkSilenceMilliseconds
            / 1000
            * sizeof(short);
        long totalByteCount = chunks.Sum(chunk => (long)chunk.Length)
            + ((long)Math.Max(0, chunks.Count - 1) * silenceByteCount);
        if (totalByteCount > int.MaxValue)
        {
            throw new InvalidDataException("Kokoro produced too much audio data.");
        }

        byte[] result = new byte[(int)totalByteCount];
        int offset = 0;
        foreach (byte[] chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length + silenceByteCount;
        }

        return result;
    }

    private static TimeSpan ValidateWave(string path)
    {
        using WaveFileReader reader = new(path);
        if (reader.WaveFormat.SampleRate != KokoroSampleRate
            || reader.WaveFormat.Channels != 1
            || reader.WaveFormat.BitsPerSample != 16)
        {
            throw new InvalidDataException(
                "Kokoro produced an unexpected audio format.");
        }

        if (reader.TotalTime <= TimeSpan.Zero)
        {
            throw new InvalidDataException("Kokoro produced an empty WAV file.");
        }

        return reader.TotalTime;
    }
}
