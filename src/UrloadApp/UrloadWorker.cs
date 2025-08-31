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
                // Using a simple for loop here even though Polly would work better.
                for (var retry = 0; retry <= opts.MaxRetries; retry++)
                {
                    try
                    {
                        await downloader.DownloadAsync(url, cancellationToken);

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
}