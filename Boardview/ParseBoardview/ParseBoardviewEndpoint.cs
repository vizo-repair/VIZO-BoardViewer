namespace VIZO.BoardViewer.Boardview.ParseBoardview;

using System;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using VIZO.BoardViewer.Serialization;

public static class ParseBoardviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/boardview/parse", async (
            BoardviewParseGate parseGate,
            HttpContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ParseBoardview");

            if (!ctx.Request.HasFormContentType || ctx.Request.Form.Files.Count == 0)
                return Results.Json(new { error = "File was not provided." }, statusCode: 400);

            var file = ctx.Request.Form.Files.GetFile("file") ?? ctx.Request.Form.Files[0];
            if (file.Length == 0)
                return Results.Json(new { error = "Empty file." }, statusCode: 400);
            if (file.Length > 100L * 1024 * 1024)
                return Results.Json(new { error = "File is too large. Maximum size is 100MB." }, statusCode: 400);

            try
            {
                byte[] bytes = new byte[file.Length];
                await using (var stream = file.OpenReadStream())
                {
                    await stream.ReadExactlyAsync(bytes, ctx.RequestAborted);
                }

                string name = Path.GetFileName(file.FileName ?? "boardview.brd");
                logger.LogInformation("[Boardview] Parse {Name} ({Kb} KB, ext {Ext})", name, bytes.Length / 1024, Path.GetExtension(name));

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                timeoutCts.CancelAfter(BoardviewFormatDetector.ParseTimeout);

                var dto = await parseGate.DetectAndParseAsync(bytes, name, msg => logger.LogInformation("{Msg}", msg), timeoutCts.Token);
                if (dto == null)
                {
                    logger.LogWarning("[Boardview] Could not recognize format for {Name}", name);
                    return Results.Json(new { error = "Could not recognize Boardview format." }, statusCode: 400);
                }

                return Results.Json(dto, VizoJsonSerializer.Options);
            }
            catch (OperationCanceledException)
            {
                return Results.Json(new { error = "Boardview parsing timed out or was canceled." }, statusCode: 408);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Boardview] Failed to parse boardview file");
                return Results.Json(new { error = "Failed to parse Boardview file." }, statusCode: 500);
            }
        });
    }
}
