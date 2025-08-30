using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class UrloadWorker(
    ILogger<UrloadWorker> log,
    IHttpClientFactory httpClientFactory,
    IOptions<UrloadOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value;
        Directory.CreateDirectory(opts.OutputDir);
        var urls = opts.Urls.Distinct().ToArray();
        if (urls.Length == 0)
        {
            log.LogWarning("No URLs configured.");
            return;
        }

        log.LogInformation("Starting downloads: {Count} urls, parallelism={Par}", urls.Length, opts.MaxConcurrency);

        var client = httpClientFactory.CreateClient("downloader");

        await Parallel.ForEachAsync(urls, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, opts.MaxConcurrency),
                CancellationToken = ct
            },
            async (url, token) =>
            {
                try
                {
                    var fileName = GetSafeFileName(url);
                    var path = Path.Combine(opts.OutputDir, fileName);

                    using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                    resp.EnsureSuccessStatusCode();

                    await using var input = await resp.Content.ReadAsStreamAsync(token);
                    await using var output = File.Create(path);
                    await input.CopyToAsync(output, token);

                    log.LogInformation("✔ Downloaded {Url} -> {Path}", url, path);
                }
                catch (OperationCanceledException) { /* shutdown */ }
                catch (Exception ex)
                {
                    log.LogError(ex, "✖ Failed to download {Url}", url);
                }
            });

        log.LogInformation("All done.");
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