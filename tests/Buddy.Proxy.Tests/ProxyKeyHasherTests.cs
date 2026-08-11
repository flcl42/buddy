namespace Buddy.Proxy.Tests;

public sealed class ProxyKeyHasherTests
{
    [Fact]
    public void GeneratedKeysAreFriendlyUppercaseTwelveLetterCodes()
    {
        string first = ProxyKeyHasher.CreateKey();
        string second = ProxyKeyHasher.CreateKey();

        Assert.True(ProxyKeyHasher.IsWellFormed(first));
        Assert.True(ProxyKeyHasher.IsWellFormed(second));
        Assert.Equal(13, first.Length);
        Assert.NotEqual(first, second);
        Assert.Equal('-', first[6]);
        Assert.All(
            first.Where(character => character != '-'),
            character => Assert.InRange(character, 'A', 'Z'));
    }

    [Fact]
    public void HashUsesDeploymentPepperAndNeverReturnsThePlainKey()
    {
        ProxyOptions options = new()
        {
            KeyPepper = new string('p', 32),
        };
        ProxyKeyHasher hasher = new(options);
        string key = "ABCDEF-GHIJKL";

        byte[] first = hasher.Hash(key);
        byte[] second = hasher.Hash(key);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.DoesNotContain(
            System.Text.Encoding.UTF8.GetBytes(key),
            first);
    }

    [Theory]
    [InlineData("abcdef-GHIJKL")]
    [InlineData("ABCDE-GHIJKL")]
    [InlineData("ABCDEF_GHIJKL")]
    [InlineData("ABCDEF-GHIJK1")]
    [InlineData("ABCDEF-GHIJKLM")]
    public void MalformedCodesAreRejected(string key)
    {
        Assert.False(ProxyKeyHasher.IsWellFormed(key));
    }
}
