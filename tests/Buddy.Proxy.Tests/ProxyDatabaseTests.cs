using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Buddy.Proxy.Tests;

public sealed class ProxyDatabaseTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "buddy-proxy-tests",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UsageIsDurableAndCountsPromptAndCompletionSeparately()
    {
        ProxyOptions options = CreateOptions();
        ProxyKeyHasher hasher = new(options);
        ProxyDatabase database = new(options, new TestEnvironment(_root));
        await database.InitializeAsync();
        string key = ProxyKeyHasher.CreateKey();
        ProxyClient created = await database.CreateClientAsync(
            "release",
            ProxyKeyHasher.GetDisplayPrefix(key),
            hasher.Hash(key),
            1_000,
            1_000_000);

        ProxyClient? authenticated = await database.FindClientAsync(hasher.Hash(key));
        ProxyClient charged = await database.RecordUsageAsync(
            Assert.IsType<ProxyClient>(authenticated),
            new ProxyUsage("request-1", "deepseek-v4-flash", 321, 123, true));

        Assert.Equal(created.Id, charged.Id);
        Assert.Equal(1, charged.RepliesUsed);
        Assert.Equal(321, charged.PromptTokensUsed);
        Assert.Equal(123, charged.CompletionTokensUsed);
        Assert.Equal(444, charged.TokensUsed);
        Assert.Equal(999, charged.RepliesRemaining);
        Assert.Equal(999_556, charged.TokensRemaining);

        ProxyDatabase reopened = new(options, new TestEnvironment(_root));
        await reopened.InitializeAsync();
        ProxyClient persisted = Assert.IsType<ProxyClient>(
            await reopened.FindClientByIdAsync(created.Id));
        Assert.Equal(charged, persisted);
    }

    [Fact]
    public async Task DisabledStatePersists()
    {
        ProxyOptions options = CreateOptions();
        ProxyKeyHasher hasher = new(options);
        ProxyDatabase database = new(options, new TestEnvironment(_root));
        await database.InitializeAsync();
        string key = ProxyKeyHasher.CreateKey();
        ProxyClient created = await database.CreateClientAsync(
            "revoked",
            ProxyKeyHasher.GetDisplayPrefix(key),
            hasher.Hash(key),
            10,
            10_000);

        Assert.True(await database.SetClientStateAsync(
            created.Id,
            ProxyKeyState.Disabled));
        ProxyClient disabled = Assert.IsType<ProxyClient>(
            await database.FindClientAsync(hasher.Hash(key)));

        Assert.Equal(ProxyKeyState.Disabled, disabled.State);
    }

    private static ProxyOptions CreateOptions() => new()
    {
        DatabasePath = "data/test.db",
        KeyPepper = new string('p', 32),
    };

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Buddy.Proxy.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = root;

        public string EnvironmentName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = root;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(root);
    }
}
