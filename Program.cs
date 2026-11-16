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

var autoShutdownEnabled = !app.Environment.IsDevelopment() && !isContainer && !noBrowser && !useLan;
var lifecycle = autoShutdownEnabled ? new BrowserLifecycleTracker(app.Lifetime) : null;

app.MapGet("/api/lifecycle/stream", async (HttpContext context) =>
{
    if (lifecycle is null)
    {
        return Results.NoContent();
    }

    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);

    lifecycle.ClientConnected();
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Browser tab closed or reloaded
    }
    finally
    {
        lifecycle.ClientDisconnected();
    }

    return Results.Empty;
});

if (!app.Environment.IsDevelopment() && !isContainer && !noBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        lifecycle?.ArmInitialTimeout();

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

sealed class BrowserLifecycleTracker(IHostApplicationLifetime lifetime)
{
    private readonly Lock _gate = new();
    private int _activeConnections;
    private bool _hasEverConnected;
    private CancellationTokenSource? _shutdownCts;

    public void ArmInitialTimeout()
    {
        ScheduleShutdown(TimeSpan.FromSeconds(60), onlyIfNeverConnected: true);
    }

    public void ClientConnected()
    {
        lock (_gate)
        {
            _hasEverConnected = true;
            _activeConnections++;
            _shutdownCts?.Cancel();
            _shutdownCts?.Dispose();
            _shutdownCts = null;
        }
    }

    public void ClientDisconnected()
    {
        lock (_gate)
        {
            _activeConnections = Math.Max(0, _activeConnections - 1);
            if (_activeConnections == 0)
            {
                ScheduleShutdownLocked(TimeSpan.FromSeconds(6), onlyIfNeverConnected: false);
            }
        }
    }

    private void ScheduleShutdown(TimeSpan delay, bool onlyIfNeverConnected)
    {
        lock (_gate)
        {
            ScheduleShutdownLocked(delay, onlyIfNeverConnected);
        }
    }

    private void ScheduleShutdownLocked(TimeSpan delay, bool onlyIfNeverConnected)
    {
        _shutdownCts?.Cancel();
        _shutdownCts?.Dispose();

        var cts = new CancellationTokenSource();
        _shutdownCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                lock (_gate)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (onlyIfNeverConnected && _hasEverConnected)
                    {
                        return;
                    }

                    if (_activeConnections == 0)
                    {
                        lifetime.StopApplication();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by new connection
            }
        }, CancellationToken.None);
    }
}
