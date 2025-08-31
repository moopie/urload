namespace UrloadApp;

public record UrloadOptions
{
    public string OutputDir { get; set; } = "downloads";
    public int MaxConcurrency { get; set; } = 4;
    public int MaxFileSizeInKb { get; set; } = 1024 * 1024;
    public int MaxRetries { get; set; } = 3;
    public int MaxBufferSizeInKb { get; set; } = 1024 * 1024;
    public int MaxTimeInMs { get; set; } = 5000;
    public List<string> Urls { get; set; } = new();
}