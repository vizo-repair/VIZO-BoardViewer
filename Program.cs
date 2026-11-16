using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using VIZO.BoardViewer.Boardview;

var useLan = args.Any(a => string.Equals(a, "--lan", StringComparison.OrdinalIgnoreCase));
var noBrowser = args.Any(a => string.Equals(a, "--no-browser", StringComparison.OrdinalIgnoreCase));
var filteredArgs = args
    .Where(a => !string.Equals(a, "--lan", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(a, "--no-browser", StringComparison.OrdinalIgnoreCase))
    .ToArray();

var contentRoot = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"))
    ? Directory.GetCurrentDirectory()
    : AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = filteredArgs,
    ContentRootPath = contentRoot
});

var hasExplicitUrls =
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")) ||
    filteredArgs.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));

if (!hasExplicitUrls)
{
    builder.WebHost.UseUrls(useLan ? "http://0.0.0.0:0" : "http://127.0.0.1:0");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100L * 1024 * 1024;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100L * 1024 * 1024;
});

builder.Services.AddBoardviewViewer();

var app = builder.Build();

var physicalWwwRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (app.Environment.IsDevelopment() && Directory.Exists(physicalWwwRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
else
{
    var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });
}

app.MapBoardviewViewerEndpoints();

var isContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!app.Environment.IsDevelopment() && !isContainer && !noBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var browserUrl = address
            .Replace("://0.0.0.0:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://[::]:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal);

        OpenBrowser(browserUrl, app.Logger);
    });
}

app.Run();

static void OpenBrowser(string url, ILogger logger)
{
    try
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", url);
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", url);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not automatically open browser at {Url}. Open it manually.", url);
    }
}
