namespace Buddy.Proxy.Tests;

public sealed class ProxyKeyHasherTests
{
    [Fact]
    public void GeneratedKeysAreCompactAndContainOneHundredTwentyEightRandomBits()
    {
        string first = ProxyKeyHasher.CreateKey();
        string second = ProxyKeyHasher.CreateKey();

        Assert.True(ProxyKeyHasher.IsWellFormed(first));
        Assert.True(ProxyKeyHasher.IsWellFormed(second));
        Assert.Equal(26, first.Length);
        Assert.NotEqual(first, second);
        Assert.StartsWith("bpk_", first, StringComparison.Ordinal);
    }

    [Fact]
    public void HashUsesDeploymentPepperAndNeverReturnsThePlainKey()
    {
        ProxyOptions options = new()
        {
            KeyPepper = new string('p', 32),
        };
        ProxyKeyHasher hasher = new(options);
        string key = "bpk_0123456789abcdefghijkl";

        byte[] first = hasher.Hash(key);
        byte[] second = hasher.Hash(key);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.DoesNotContain(
            System.Text.Encoding.UTF8.GetBytes(key),
            first);
    }
}
