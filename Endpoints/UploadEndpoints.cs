using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Api.Extensions;
using Api.Infrastructure;
using Api.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(
        this IEndpointRouteBuilder app)
    {


        // POST /uploads
        app.MapPost("/uploads", HandlePostUploadAsync)
        .Accepts<IFormFile>("multipart/form-data")
        .RequireRateLimiting("Upload")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status415UnsupportedMediaType);

        // GET /uploads/{id}/summary — reads from ABSOLUTE path
        app.MapGet("/uploads/{uploadId:guid}/summary", async (Guid uploadId, HttpContext ctx, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await uploads.CanAccessAsync(uploadId, me, ctx.RequestAborted)) return Results.NotFound();

            var json = await storage.ReadTextAsync(uploadId, ".summary.json", ctx.RequestAborted);
            if (json is null) return Results.NotFound();
            return Results.Text(json, "application/json");
        });

        app.MapPost("/uploads/{uploadId:guid}/layout/analyze", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await uploads.CanAccessAsync(uploadId, me, ctx.RequestAborted)) return Results.NotFound(new { error = "PDF not found" });

            if (!await storage.PdfExistsAsync(uploadId, ctx.RequestAborted)) return Results.NotFound(new { error = "PDF not found" });

            var manifest = await DocumentLayoutAnalyzer.AnalyzeAndSaveAsync(uploadId, storage, ctx.RequestAborted);
            return Results.Json(manifest);
        });

        app.MapGet("/uploads/{uploadId:guid}/layout", async (Guid uploadId, HttpContext ctx, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await uploads.CanAccessAsync(uploadId, me, ctx.RequestAborted)) return Results.NotFound(new { error = "layout not found" });

            var json = await storage.ReadTextAsync(uploadId, ".layout.json", ctx.RequestAborted);
            if (json is null) return Results.NotFound(new { error = "layout not found" });
            return Results.Text(json, "application/json");
        });

        // GET /cases — per-user list of uploads
        app.MapGet("/cases", HandleGetCasesAsync);

        // GET/HEAD /uploads/{id}.pdf — serves from ABSOLUTE path (use Results.File)
        app.MapMethods("/uploads/{uploadId:guid}.pdf", new[] { "GET", "HEAD" }, async (Guid uploadId, HttpContext ctx, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            try
            {
                var me = ctx.GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
                if (!await uploads.CanAccessAsync(uploadId, me, ctx.RequestAborted)) return Results.NotFound();

                var path = await storage.GetPdfPathAsync(uploadId, ctx.RequestAborted);
                if (path is null) return Results.NotFound();
                return Results.File(path, "application/pdf", enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PDF GET] {uploadId} failed: {ex.GetType().Name} - {ex.Message}");
                return Results.StatusCode(500);
            }
        });


        return app;
    }

    public static async Task<IResult> HandleGetCasesAsync(
        HttpContext context,
        IDocumentStorage storage,
        IUploadRepository uploads)
    {
        var userId = context.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var ownedUploadIds = await uploads.GetOwnedUploadIdsAsync(userId, context.RequestAborted);
        var cases = new List<CaseDto>();

        foreach (var ownedUploadId in ownedUploadIds)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            if (!Guid.TryParse(ownedUploadId, out var uploadId))
            {
                continue;
            }

            var summaryJson = await storage.ReadTextAsync(
                uploadId,
                DocumentArtifactSuffixes.Summary,
                context.RequestAborted);
            if (summaryJson is null)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(summaryJson);
                var root = doc.RootElement;

                string id = root.TryGetProperty("uploadId", out var pid)
                    ? (pid.ValueKind == JsonValueKind.String ? pid.GetString()! : pid.ToString())
                    : "";
                if (string.IsNullOrWhiteSpace(id) || !ownedUploadIds.Contains(id))
                {
                    continue;
                }

                string name = root.TryGetProperty("fileName", out var pn) ? (pn.GetString() ?? "") : "";
                int pages = root.TryGetProperty("pages", out var pp) && pp.TryGetInt32(out var p) ? p : 0;
                double sizeMB = root.TryGetProperty("fileSizeMB", out var ps) && ps.TryGetDouble(out var s) ? s : 0.0;

                int images = 0;
                if (root.TryGetProperty("counts", out var counts) && counts.TryGetProperty("images", out var ci))
                    ci.TryGetInt32(out images);

                string uploadedAt = root.TryGetProperty("uploadedAt", out var pu) && pu.ValueKind == JsonValueKind.String
                    ? (pu.GetString() ?? "")
                    : "";

                cases.Add(new CaseDto(id, name, pages, images, sizeMB, uploadedAt));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CASES] Skipping '{ownedUploadId}': {ex.GetType().Name} - {ex.Message}");
            }
        }

        var ordered = cases
            .OrderByDescending(c => DateTime.TryParse(c.UploadedAt, out var dt) ? dt : DateTime.MinValue)
            .ToList();

        return Results.Json(ordered);
    }

    public static async Task<IResult> HandlePostUploadAsync(
        HttpRequest request,
        HttpContext context,
        IUploadProcessingService uploadProcessingService)
    {
        var ownerId = context.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Use multipart/form-data with a file field named 'file'." });
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "The multipart upload could not be read or exceeds the configured size limit." });
        }

        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Results.BadRequest(new { error = "The multipart field 'file' is required." });
        }

        if (file.Length == 0)
        {
            return Results.BadRequest(new { error = "The multipart field 'file' must contain a non-empty PDF." });
        }

        var result = await uploadProcessingService.ProcessAsync(ownerId, file, context.RequestAborted);
        if (result.Succeeded && result.UploadId is Guid uploadId)
        {
            return Results.Created($"/uploads/{uploadId}/summary", new { uploadId });
        }

        var errorBody = new
        {
            error = result.Error,
            maxBytes = result.MaxBytes,
            pages = result.Pages,
            maxPages = result.MaxPages
        };

        return result.RejectionReason == UploadRejectionReason.UnsupportedMediaType
            ? Results.Json(errorBody, statusCode: StatusCodes.Status415UnsupportedMediaType)
            : Results.BadRequest(errorBody);
    }
}
