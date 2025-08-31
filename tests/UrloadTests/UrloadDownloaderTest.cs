using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using UrloadApp;

namespace UrloadApp.Tests;

[TestFixture]
public class UrloadDownloaderTests
{
    private string _tempDir = null!;
    private UrloadOptions _options = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private Mock<ILogger<UrloadWorker>> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _options = new UrloadOptions
        {
            OutputDir = _tempDir,
            MaxBufferSizeInKb = 4,
            MaxFileSizeInKb = 64,
            MaxTimeInMs = 2000
        };

        _httpClientFactory = new Mock<IHttpClientFactory>();
        _logger = new Mock<ILogger<UrloadWorker>>();
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private IUrloadDownloader CreateDownloader(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        _httpClientFactory.Setup(f => f.CreateClient("downloader")).Returns(client);
        var opts = Options.Create(_options);
        return new UrloadDownloader(_httpClientFactory.Object, opts, _logger.Object);
    }

    [Test]
    public async Task DownloadAsync_WritesFileAndReturnsPath()
    {
        // Arrange: small test payload
        var data = Encoding.UTF8.GetBytes("Hello World");
        var handler = new StubHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent(data)
        });

        var downloader = CreateDownloader(handler);
        var url = "http://example.com/test.txt";

        // Act
        var path = await downloader.DownloadAsync(url, CancellationToken.None);

        // Assert
        Assert.That(File.Exists(path), Is.True);
        var content = await File.ReadAllTextAsync(path);
        Assert.That(content, Is.EqualTo("Hello World"));
    }

    [Test]
    public void DownloadAsync_ThrowsIfFileTooLarge()
    {
        // Arrange: generate payload larger than allowed
        _options.MaxFileSizeInKb = 1; // 1 KB
        var data = new byte[2 * 1024]; // 2 KB
        var handler = new StubHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent(data)
        });

        var downloader = CreateDownloader(handler);
        var url = "http://example.com/big.bin";

        // Act + Assert
        Assert.ThrowsAsync<IOException>(async () =>
            await downloader.DownloadAsync(url, CancellationToken.None));
    }

    [Test]
    public void DownloadAsync_ThrowsIfTakesTooLong()
    {
        // Arrange: artificially slow stream
        _options.MaxTimeInMs = 10; // very low threshold
        var handler = new SlowHandler(delayPerReadMs: 50, totalBytes: 1024);

        var downloader = CreateDownloader(handler);
        var url = "http://example.com/slow.bin";

        // Act + Assert
        Assert.ThrowsAsync<IOException>(async () =>
            await downloader.DownloadAsync(url, CancellationToken.None));
    }

    // --- Helpers ---

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StubHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly int _delayPerReadMs;
        private readonly int _totalBytes;

        public SlowHandler(int delayPerReadMs, int totalBytes)
        {
            _delayPerReadMs = delayPerReadMs;
            _totalBytes = totalBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new PushStreamContent(async (stream, _, __) =>
            {
                var buffer = new byte[256];
                int written = 0;
                while (written < _totalBytes)
                {
                    await Task.Delay(_delayPerReadMs, cancellationToken);
                    await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                    written += buffer.Length;
                }
                await stream.FlushAsync(cancellationToken);
                stream.Close();
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }
}