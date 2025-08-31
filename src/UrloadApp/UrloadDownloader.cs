using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UrloadApp;

public interface IUrloadDownloader
{
    public Task<string> DownloadAsync(string url, CancellationToken cancellationToken);
}

public class UrloadDownloader(IHttpClientFactory httpClient, IOptions<UrloadOptions> options, ILogger<UrloadWorker> log) : IUrloadDownloader
{
    private readonly ConcurrentBag<(LogLevel Level, string Message, object?[] Args)> Messages = new();
    
    public async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        void MessageBuffer(LogLevel level, string message, params object?[] args)
            => Messages.Add((level, message, args));
        
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        
        var opts = options.Value;
        var fileName = GetSafeFileName(url);
        var path = Path.Combine(opts.OutputDir, fileName);
                    
        MessageBuffer(LogLevel.Information, "Starting download: {Url}", url);
                    
        var buffer = new byte[opts.MaxBufferSizeInKb * 1024];
        long totalRead = 0;
        int read;
        var sw = Stopwatch.StartNew();
                    
        var client = httpClient.CreateClient("downloader");
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalRead += read;
            if (totalRead > opts.MaxFileSizeInKb * 1024)
            {
                MessageBuffer(LogLevel.Warning, "[x] File {Url} exceeded size limit ({Max} bytes). Aborting.", url, opts.MaxFileSizeInKb);
    
                FlushLogs(Messages);

                sw.Stop();
                throw new IOException($"File too large: exceeded {opts.MaxFileSizeInKb} bytes");
            }

            if (sw.ElapsedMilliseconds > opts.MaxTimeInMs)
            {
                MessageBuffer(LogLevel.Warning, "[x] File {Url} exceeded time limit of {ElapsedMs}", url, sw.ElapsedMilliseconds);

                FlushLogs(Messages);

                sw.Stop();
                throw new IOException($"File took too long to download: exceeded {opts.MaxTimeInMs} milliseconds");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        
        MessageBuffer(LogLevel.Information, "[v] Downloaded {Url} -> {Path} in {Time}ms ({Size}b)", url, path, sw.ElapsedMilliseconds, totalRead);
        sw.Stop();

        // Flush all buffered logs.
        FlushLogs(Messages);

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
    
    private void FlushLogs(IEnumerable<(LogLevel Level, string Message, object?[] Args)> messages)
    {
        foreach (var (level, msg, args) in messages)
        {
            log.Log(level, msg, args);
        }
    }
}