using Buddy.Speech;

namespace Buddy.IntegrationTests;

public sealed class KokoroPhoneticTranscriptionServiceTests
{
    [Fact]
    public async Task EspeakBackendProducesReadableIpaForNames()
    {
        KokoroPhoneticTranscriptionService service = new();

        string phonetics = await service.TranscribeAsync(
            "Let me introduce myself. My name is Alexey.");

        Assert.Contains('ˈ', phonetics);
        Assert.Contains("ɛ", phonetics, StringComparison.Ordinal);
        Assert.DoesNotContain('❓', phonetics);
        Assert.DoesNotContain("Alexey", phonetics, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Guten Tag", "de-DE")]
    [InlineData("Buenos días", "es-ES")]
    [InlineData("Bonjour", "fr-FR")]
    [InlineData("Добры дзень", "be-BY")]
    public async Task EspeakBackendSupportsConfiguredDialogLanguages(
        string text,
        string locale)
    {
        KokoroPhoneticTranscriptionService service = new();

        string phonetics = await service.TranscribeAsync(text, locale);

        Assert.False(string.IsNullOrWhiteSpace(phonetics));
        Assert.NotEqual(text, phonetics);
    }
}
