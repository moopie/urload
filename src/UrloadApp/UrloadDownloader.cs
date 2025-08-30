using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UrloadApp;

public interface IUrloadDownloader
{
    public Task<string> DownloadAsync(string url, CancellationToken cancellationToken);
}

public class UrloadDownloader(IHttpClientFactory httpClient, IOptions<UrloadOptions> options, ILogger<UrloadWorker> log) : IUrloadDownloader
{
    public async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        
        var opts = options.Value;
        var fileName = GetSafeFileName(url);
        var path = Path.Combine(opts.OutputDir, fileName);
                    
        log.LogInformation($"Starting download: {url}");
                    
        var buffer = new byte[opts.MaxBufferSizeInKb]; // default buffer size used by CopyToAsync
        long totalRead = 0;
        int read;
                    
        var client = httpClient.CreateClient("downloader");
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalRead += read;
            if (totalRead > opts.MaxFileSizeInKb * 1024)
            {
                log.LogWarning("✖ File {Url} exceeded size limit ({Max} bytes). Aborting.", url, opts.MaxFileSizeInKb);
                throw new IOException($"File too large: exceeded {opts.MaxFileSizeInKb} bytes");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return path;
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