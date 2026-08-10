using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Buddy.Core.Abstractions;
using Buddy.Speech;

namespace Buddy.IntegrationTests;

public sealed class VerifiedLocalModelManagerTests
{
    [Fact]
    public async Task EnsureInstalledResumesAndPinsVerifiedModel()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            byte[] payload = Enumerable.Range(0, 32_000)
                .Select(index => (byte)(index % 251))
                .ToArray();
            VerifiedLocalModelManager.LocalModelManifest manifest = CreateManifest(payload);
            string partialPath = Path.Combine(root, manifest.FileName + ".partial");
            await File.WriteAllBytesAsync(partialPath, payload.AsMemory(0, 7_321).ToArray());

            RangePayloadHandler handler = new(payload);
            using HttpClient client = new(handler);
            VerifiedLocalModelManager manager = CreateManager(root, client, manifest);
            List<double> progressValues = [];

            await manager.EnsureInstalledAsync(
                manifest.Id,
                new Progress<double>(progressValues.Add));

            Assert.Equal(7_321, handler.RequestedOffset);
            Assert.Equal(payload, await File.ReadAllBytesAsync(
                Path.Combine(root, manifest.FileName)));
            Assert.False(File.Exists(partialPath));
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains(progressValues, value => value >= 1);

            IReadOnlyList<LocalModelInfo> models = await manager.GetModelsAsync();
            LocalModelInfo installed = Assert.Single(models);
            Assert.Equal(LocalModelStatus.Ready, installed.Status);

            await manager.EnsureInstalledAsync(manifest.Id);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureInstalledRejectsContentWithWrongHash()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            byte[] expected = [1, 2, 3, 4, 5];
            byte[] received = [1, 2, 3, 4, 6];
            VerifiedLocalModelManager.LocalModelManifest manifest = CreateManifest(expected);
            using HttpClient client = new(new RangePayloadHandler(received));
            VerifiedLocalModelManager manager = CreateManager(root, client, manifest);

            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => manager.EnsureInstalledAsync(manifest.Id));

            Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, manifest.FileName)));
            Assert.False(File.Exists(Path.Combine(root, manifest.FileName + ".partial")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static VerifiedLocalModelManager CreateManager(
        string root,
        HttpClient client,
        VerifiedLocalModelManager.LocalModelManifest manifest)
    {
        IReadOnlyDictionary<string, VerifiedLocalModelManager.LocalModelManifest> models =
            new Dictionary<string, VerifiedLocalModelManager.LocalModelManifest>(
                StringComparer.Ordinal)
            {
                [manifest.Id] = manifest,
            };
        return new VerifiedLocalModelManager(root, client, models);
    }

    private static VerifiedLocalModelManager.LocalModelManifest CreateManifest(byte[] payload)
    {
        return new VerifiedLocalModelManager.LocalModelManifest(
            "test-model",
            "Test model",
            "test-model.bin",
            new Uri("https://models.example.test/test-model.bin"),
            payload.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(payload)));
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

    private sealed class RangePayloadHandler(byte[] payload) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public long? RequestedOffset { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            RequestedOffset = request.Headers.Range?.Ranges.Single().From;
            int offset = checked((int)(RequestedOffset ?? 0));
            byte[] content = payload.AsMemory(offset).ToArray();
            HttpResponseMessage response = new(
                offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
                RequestMessage = request,
            };
            response.Content.Headers.ContentLength = content.Length;
            if (offset > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    offset,
                    payload.Length - 1,
                    payload.Length);
            }

            return Task.FromResult(response);
        }
    }
}
