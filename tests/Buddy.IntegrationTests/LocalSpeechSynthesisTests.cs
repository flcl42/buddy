using Buddy.Audio.Windows;
using Buddy.Core.Abstractions;
using Buddy.Speech;
using NAudio.Wave;

namespace Buddy.IntegrationTests;

public sealed class LocalSpeechSynthesisTests
{
    [Fact]
    public void WavePlaybackSourceReadsAndSeeksGeneratedAudio()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "generated.wav");
            using (WaveFileWriter writer = new(path, new WaveFormat(24_000, 16, 1)))
            {
                for (int index = 0; index < 24_000; index++)
                {
                    writer.WriteSample(
                        0.2f * MathF.Sin(2 * MathF.PI * 440 * index / 24_000));
                }
            }

            using WavePlaybackSource source = new(path);
            Assert.Equal(TimeSpan.FromSeconds(1), source.Duration);
            Assert.Equal(TimeSpan.Zero, source.Position);

            source.Seek(TimeSpan.FromMilliseconds(500));
            Assert.InRange(
                source.Position,
                TimeSpan.FromMilliseconds(499),
                TimeSpan.FromMilliseconds(501));

            byte[] buffer = new byte[4_800];
            Assert.Equal(buffer.Length, source.Read(buffer, 0, buffer.Length));
            Assert.True(source.Position > TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KokoroServiceExposesCuratedVoicesAndRequiresVerifiedModel()
    {
        using KokoroSpeechSynthesisService service = new(
            new MissingModelManager());
        IReadOnlyList<SpeechVoice> voices = await service.GetVoicesAsync();

        Assert.Collection(
            voices,
            voice => Assert.Equal("af_heart", voice.Id),
            voice => Assert.Equal("am_michael", voice.Id),
            voice => Assert.Equal("bf_emma", voice.Id),
            voice => Assert.Equal("bm_george", voice.Id));

        LocalModelNotInstalledException error =
            await Assert.ThrowsAsync<LocalModelNotInstalledException>(
                () => service.SynthesizeAsync(
                    "A short test.",
                    Path.Combine(Path.GetTempPath(), "buddy-missing-model.wav"),
                    new SpeechSynthesisOptions("af_heart")));
        Assert.Equal(LocalSpeechModels.KokoroEnglishV1, error.ModelId);
    }

    [Theory]
    [InlineData(
        "It isn’t right, doesn't help, and won't work.",
        "It is not right, does not help, and will not work.")]
    [InlineData(
        "It isn't right, can't help, and won't work.",
        "It is not right, cannot help, and will not work.")]
    public void ContractionsUseUnambiguousSpokenForms(
        string text,
        string expected)
    {
        string prepared = KokoroSynthesisTextPreprocessor
            .ApplyEnglishContractionSpeechForms(text);

        Assert.Equal(expected, prepared);
    }

    [Fact]
    public void ContractionPreparationPreservesOverridesAndPossessives()
    {
        const string text =
            "[isn't](/custom/) Alex's choice isn't affected by she'll.";

        string prepared = KokoroSynthesisTextPreprocessor
            .ApplyEnglishContractionSpeechForms(text);

        Assert.Equal(
            "[isn't](/custom/) Alex's choice is not affected by she'll.",
            prepared);
    }

    [Fact]
    public void PreparedContractionDoesNotInjectInlinePhonemeTokens()
    {
        string prepared = KokoroSynthesisTextPreprocessor
            .ApplyEnglishContractionSpeechForms("It isn't right.");

        Assert.Equal("It is not right.", prepared);
        Assert.DoesNotContain("[", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("/", prepared, StringComparison.Ordinal);
    }

    [Fact]
    public void LongQuotedSpeechIsSplitWithoutDroppingOrDuplicatingWords()
    {
        const string text =
            "Great way to wrap up. Here are three advanced but genuinely "
            + "common words that natives use in everyday conversation. "
            + "\"Nuanced\" means something has many subtle layers or "
            + "complexities. \"Her opinion on the issue is very nuanced; "
            + "she sees both sides.\" \"Implicitly\" means in a way that is "
            + "understood without being stated. \"He implicitly trusted her "
            + "with the project.\" A paradox is a situation that seems "
            + "contradictory but might be true. \"It is a paradox that "
            + "working less can sometimes make you more productive.\" "
            + "These words are natural in discussions about work, "
            + "relationships, or ideas. You did really well today.";

        IReadOnlyList<string> chunks = KokoroSynthesisTextChunker.Split(
            text,
            maximumCharacters: 180);

        Assert.True(chunks.Count >= 4);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 180));
        Assert.Equal(
            GetWords(text),
            GetWords(string.Join(' ', chunks)));
        Assert.Equal(
            GetPunctuation(text),
            GetPunctuation(string.Concat(chunks)));
    }

    [Fact]
    public void SpeechChunkingPrefersACompleteQuotedSentence()
    {
        string prefix = new('A', 105);
        string text = $"{prefix} \"Quoted words stay complete.\" "
            + new string('B', 100);

        IReadOnlyList<string> chunks = KokoroSynthesisTextChunker.Split(
            text,
            maximumCharacters: 150);

        Assert.Equal(2, chunks.Count);
        Assert.EndsWith(
            "\"Quoted words stay complete.\"",
            chunks[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public void KokoroRuntimeAssetsSelectCompleteCandidate()
    {
        string root = CreateTemporaryDirectory();
        string incomplete = CreateTemporaryDirectory();
        try
        {
            string voices = Path.Combine(root, "voices");
            string espeak = Path.Combine(root, "espeak");
            Directory.CreateDirectory(voices);
            Directory.CreateDirectory(Path.Combine(espeak, "espeak-ng-data"));
            File.WriteAllText(
                Path.Combine(espeak, "espeak-ng-win-amd64.dll"),
                "test");
            foreach (string voice in
                     new[]
                     {
                         "af_heart.npy",
                         "am_michael.npy",
                         "bf_emma.npy",
                         "bm_george.npy",
                     })
            {
                File.WriteAllText(Path.Combine(voices, voice), "test");
            }

            string selected = KokoroRuntimeAssets.FindRootPath(
                [null, incomplete, root]);

            Assert.Equal(Path.GetFullPath(root), selected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incomplete, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string[] GetWords(string text)
    {
        return System.Text.RegularExpressions.Regex
            .Matches(text, @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?")
            .Select(match => match.Value)
            .ToArray();
    }

    private static char[] GetPunctuation(string text)
    {
        return text.Where(character =>
                !char.IsLetterOrDigit(character)
                && !char.IsWhiteSpace(character))
            .ToArray();
    }

    private sealed class MissingModelManager : ILocalModelManager
    {
        public Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<LocalModelInfo> models =
            [
                new(
                    LocalSpeechModels.KokoroEnglishV1,
                    "Kokoro 82M English",
                    325_508_342,
                    new string('0', 64),
                    LocalModelStatus.NotInstalled,
                    null),
            ];
            return Task.FromResult(models);
        }

        public Task EnsureInstalledAsync(
            string modelId,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
