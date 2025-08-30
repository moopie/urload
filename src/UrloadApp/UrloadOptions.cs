public sealed class UrloadOptions
{
    public string OutputDir { get; set; } = "downloads";
    public int MaxConcurrency { get; set; } = 4;
    public List<string> Urls { get; set; } = new();
}