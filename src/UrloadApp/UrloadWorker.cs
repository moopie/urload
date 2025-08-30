using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UrloadApp;

public class UrloadWorker(
    ILogger<UrloadWorker> log,
    IHttpClientFactory httpClient,
    IOptions<UrloadOptions> options,
    IHostApplicationLifetime lifetime,
    IUrloadDownloader downloader) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value;
        Directory.CreateDirectory(opts.OutputDir);
        var urls = opts.Urls.Distinct().ToArray();
        if (urls.Length == 0)
        {
            log.LogWarning("No URLs configured.");
            throw new Exception("No URLs configured.");
        }

        log.LogInformation("Starting downloads: {Count} urls, parallelism={Par}", urls.Length, opts.MaxConcurrency);

        var client = httpClient.CreateClient("downloader");

        await Parallel.ForEachAsync(urls, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, opts.MaxConcurrency),
                CancellationToken = ct
            },
            async (url, cancellationToken) =>
            {
                for (var retry = 0; retry <= opts.MaxRetries; retry++)
                {
                    try
                    {
                        var path = await downloader.DownloadAsync(url, cancellationToken);

                        log.LogInformation("✔ Downloaded {Url} -> {Path}", url, path);
                        retry = opts.MaxRetries + 1;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (retry == opts.MaxRetries)
                        {
                            throw;
                        }
                        log.LogError(ex, "✖ Failed to download {Url}, attempt #{retry}", url, retry);
                    }
                }
            });

        log.LogInformation("All done.");
        lifetime.StopApplication();
    }

    private static string GetSafeFileName(string url)
    {
        // Try to use the last path segment; fall back to a hash if empty.
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(name))
                name = "file";
            // Ensure unique-ish by appending a short hash of the URL
            var suffix = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..8];
            name = $"{name}-{suffix}";
            // Keep an extension if present in URL
            var ext = Path.GetExtension(uri.LocalPath);
            if (!string.IsNullOrEmpty(ext) && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                name += ext;
            // Replace illegal chars just in case
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
        catch
        {
            return $"download-{Guid.NewGuid():N}";
        }
    }
}