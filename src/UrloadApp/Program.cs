using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrloadApp;

await Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<UrloadOptions>(context.Configuration.GetSection("Download"));
        
        services.AddTransient<IUrloadDownloader, UrloadDownloader>();
        
        services.AddHttpClient("urload-app", c =>
        {
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UrlDownloader", "1.0"));
            c.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHostedService<UrloadWorker>();
    })
    .ConfigureLogging(b => b.AddConsole())
    .RunConsoleAsync();