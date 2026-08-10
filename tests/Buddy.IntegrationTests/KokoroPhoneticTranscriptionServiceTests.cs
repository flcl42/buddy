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
}
