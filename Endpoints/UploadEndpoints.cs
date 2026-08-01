using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Api.Extensions;
using Api.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(
        this IEndpointRouteBuilder app,
        string connString)
    {


        // POST /uploads  (save PDF + minimal summary) — uses ABSOLUTE uploads path
        app.MapPost("/uploads", async (HttpRequest request, HttpContext ctx, IWebHostEnvironment env, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            var ownerId = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return Results.Unauthorized();
            }

            if (!request.HasFormContentType)
                return Results.BadRequest("Use multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
            {
                Console.WriteLine($"[UPLOAD DEBUG] ContentType={request.ContentType} Keys=[{string.Join(",", form.Keys)}] Files={form.Files.Count}");
                return Results.BadRequest($"No file. ContentType={request.ContentType}; Keys=[{string.Join(",", form.Keys)}]; Files={form.Files.Count}");
            }

            var maxUploadBytes = GetLongEnv("MAX_UPLOAD_BYTES", 25L * 1024L * 1024L);
            if (file.Length > maxUploadBytes)
            {
                return Results.BadRequest(new
                {
                    error = "File is too large.",
                    maxBytes = maxUploadBytes
                });
            }

            // PDF-only guard
            var isPdf = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf)
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

            var uploadId = Guid.NewGuid();

            var filePath = await storage.SavePdfAsync(uploadId, file, ctx.RequestAborted);

            // --- Minimal analysis: pages + raster images + file size + uploadedAt ---
            var uploadedAt = DateTime.UtcNow;

            var fi = new FileInfo(filePath);
            long fileSizeBytes = fi.Length;
            double fileSizeMB = Math.Round(fileSizeBytes / (1024.0 * 1024.0), 2);

            int pages;
            using (var doc = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(filePath)))
            {
                pages = doc.GetNumberOfPages();
            }

            var maxPages = GetIntEnv("MAX_UPLOAD_PAGES", 100);
            if (pages > maxPages)
            {
                try
                {
                    await storage.DeleteArtifactsAsync(uploadId, ctx.RequestAborted);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UPLOAD WARNING] Could not delete rejected PDF {filePath}: {ex.Message}");
                }

                return Results.BadRequest(new
                {
                    error = "PDF has too many pages.",
                    pages,
                    maxPages
                });
            }

            int images = 0;

            try
            {
                images = PdfImageUtils.CountRasterImagesExact(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PDF IMAGE COUNT WARNING] Could not count images for {filePath}: {ex.Message}");
            }
            var figures = 0;
            var tables = 0;
            try
            {
                var layout = await DocumentLayoutAnalyzer.AnalyzeAndSaveAsync(uploadId, env);
                figures = layout.Captions.Count(c => c.Kind.Equals("figure", StringComparison.OrdinalIgnoreCase));
                tables = layout.Captions.Count(c => c.Kind.Equals("table", StringComparison.OrdinalIgnoreCase)) + layout.Tables.Count;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LAYOUT WARNING] Could not analyze layout for {uploadId}: {ex.GetType().Name} - {ex.Message}");
            }

            var summary = new
            {
                uploadId,
                fileName = file.FileName,
                fileSizeBytes,
                fileSizeMB,
                pages,
                counts = new { images, figures, tables },
                uploadedAt = uploadedAt.ToString("o"),
                generatedAt = DateTime.UtcNow.ToString("o")
            };

            await storage.WriteJsonAsync(uploadId, ".summary.json", summary, ctx.RequestAborted);

            // Use the original filename from the upload (e.g. "Healthcare Case.pdf")
            var originalFileName = Path.GetFileName(file.FileName);


            await uploads.CreateAsync(
                new UploadMetadata(uploadId, ownerId, filePath, originalFileName ?? "", DateTime.UtcNow),
                ctx.RequestAborted);

            return Results.Json(new { uploadId });


           
        })
        .Accepts<IFormFile>("multipart/form-data")
        .RequireRateLimiting("Upload")
        .Produces(StatusCodes.Status200OK)
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

            var manifest = await DocumentLayoutAnalyzer.AnalyzeAndSaveAsync(uploadId, env);
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
        app.MapGet("/cases", async (HttpContext ctx, IDocumentStorage storage, IUploadRepository uploads) =>
        {
            // 1) Get current userId from JWT middleware
            var userId = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                // Should not normally happen because of auth middleware,
                // but this keeps things explicit.
                return Results.Unauthorized();
            }

            var allowedUploadIds = await uploads.GetOwnedUploadIdsAsync(userId, ctx.RequestAborted);

            // 3) Scan uploads folder as before, but filter to this user's UploadIds
            var cases = new List<CaseDto>();

            await foreach (var summaryFile in storage.EnumerateSummariesAsync(ctx.RequestAborted))
            {
                try
                {
                    using var doc = JsonDocument.Parse(summaryFile.Json);
                    var root = doc.RootElement;

                    string id = root.TryGetProperty("uploadId", out var pid)
                        ? (pid.ValueKind == JsonValueKind.String ? pid.GetString()! : pid.ToString())
                        : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    // ?? New: if this upload does NOT belong to the current user, skip it
                    if (!allowedUploadIds.Contains(id))
                        continue;

                    string name = root.TryGetProperty("fileName", out var pn) ? (pn.GetString() ?? "") : "";
                    int pages = root.TryGetProperty("pages", out var pp) && pp.TryGetInt32(out var p) ? p : 0;
                    double sizeMB = root.TryGetProperty("fileSizeMB", out var ps) && ps.TryGetDouble(out var s) ? s : 0.0;

                    int images = 0;
                    if (root.TryGetProperty("counts", out var counts) && counts.TryGetProperty("images", out var ci))
                        ci.TryGetInt32(out images);

                    string uploadedAt = root.TryGetProperty("uploadedAt", out var pu) && pu.ValueKind == JsonValueKind.String
                        ? (pu.GetString() ?? "")
                        : summaryFile.LastModifiedUtc.ToString("o");

                    cases.Add(new CaseDto(id, name, pages, images, sizeMB, uploadedAt));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CASES] Skipping '{summaryFile.UploadId}': {ex.GetType().Name} - {ex.Message}");
                }
            }

            var ordered = cases
                .OrderByDescending(c => DateTime.TryParse(c.UploadedAt, out var dt) ? dt : DateTime.MinValue)
                .ToList();

            return Results.Json(ordered);
        });

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

    private static long GetLongEnv(string name, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static int GetIntEnv(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
