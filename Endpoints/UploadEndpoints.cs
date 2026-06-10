using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Api.Extensions;

namespace Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(
        this IEndpointRouteBuilder app,
        string connString)
    {


        // POST /uploads  (save PDF + minimal summary) — uses ABSOLUTE uploads path
        app.MapPost("/uploads", async (HttpRequest request, HttpContext ctx, IWebHostEnvironment env) =>
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

            // PDF-only guard
            var isPdf = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf)
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

            var uploadId = Guid.NewGuid();

            // ABSOLUTE uploads folder
            var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
            Directory.CreateDirectory(uploadsRoot);

            var filePath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");

            // Save file
            await using (var outStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await file.CopyToAsync(outStream);
            }

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

            var summaryPath = Path.Combine(uploadsRoot, $"{uploadId}.summary.json");
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary));

            // Use the original filename from the upload (e.g. "Healthcare Case.pdf")
            var originalFileName = Path.GetFileName(file.FileName);


            // persist ownership (per-user scoping)
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO Uploads (UploadId, UserId, FilePath, OriginalFileName, CreatedAt)
            VALUES ($u, $usr, $path, $name, $ts)";
                cmd.Parameters.AddWithValue("$u", uploadId);
                cmd.Parameters.AddWithValue("$usr", ownerId);
                cmd.Parameters.AddWithValue("$path", filePath);
                cmd.Parameters.AddWithValue("$name", originalFileName ?? "");
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Json(new { uploadId });


           
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status415UnsupportedMediaType);

        // GET /uploads/{id}/summary — reads from ABSOLUTE path
        app.MapGet("/uploads/{uploadId:guid}/summary", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await CanAccessUploadAsync(connString, uploadId, me)) return Results.NotFound();

            var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.summary.json");
            if (!File.Exists(path)) return Results.NotFound();
            var json = await File.ReadAllTextAsync(path);
            return Results.Text(json, "application/json");
        });

        app.MapPost("/uploads/{uploadId:guid}/layout/analyze", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await CanAccessUploadAsync(connString, uploadId, me)) return Results.NotFound(new { error = "PDF not found" });

            var pdfPath = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
            if (!File.Exists(pdfPath)) return Results.NotFound(new { error = "PDF not found" });

            var manifest = await DocumentLayoutAnalyzer.AnalyzeAndSaveAsync(uploadId, env);
            return Results.Json(manifest);
        });

        app.MapGet("/uploads/{uploadId:guid}/layout", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (!await CanAccessUploadAsync(connString, uploadId, me)) return Results.NotFound(new { error = "layout not found" });

            var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.layout.json");
            if (!File.Exists(path)) return Results.NotFound(new { error = "layout not found" });

            var json = await File.ReadAllTextAsync(path);
            return Results.Text(json, "application/json");
        });

        // GET /cases — per-user list of uploads
        app.MapGet("/cases", async (HttpContext ctx, IWebHostEnvironment env) =>
        {
            // 1) Get current userId from JWT middleware
            var userId = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                // Should not normally happen because of auth middleware,
                // but this keeps things explicit.
                return Results.Unauthorized();
            }

            // 2) Load this user's uploadIds from the Uploads table
            var allowedUploadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT UploadId FROM Uploads WHERE UserId = $userId";
                cmd.Parameters.AddWithValue("$userId", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var uploadId = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(uploadId))
                            allowedUploadIds.Add(uploadId);
                    }
                }
            }

            // 3) Scan uploads folder as before, but filter to this user's UploadIds
            var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
            Directory.CreateDirectory(uploadsRoot);

            var cases = new List<CaseDto>();

            foreach (var path in Directory.EnumerateFiles(uploadsRoot, "*.summary.json"))
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    using var doc = JsonDocument.Parse(fs);
                    var root = doc.RootElement;

                    string id = root.TryGetProperty("uploadId", out var pid)
                        ? (pid.ValueKind == JsonValueKind.String ? pid.GetString()! : pid.ToString())
                        : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    // 👇 New: if this upload does NOT belong to the current user, skip it
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
                        : File.GetLastWriteTimeUtc(path).ToString("o");

                    cases.Add(new CaseDto(id, name, pages, images, sizeMB, uploadedAt));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CASES] Skipping '{Path.GetFileName(path)}': {ex.GetType().Name} - {ex.Message}");
                }
            }

            var ordered = cases
                .OrderByDescending(c => DateTime.TryParse(c.UploadedAt, out var dt) ? dt : DateTime.MinValue)
                .ToList();

            return Results.Json(ordered);
        });

        // GET/HEAD /uploads/{id}.pdf — serves from ABSOLUTE path (use Results.File)
        app.MapMethods("/uploads/{uploadId:guid}.pdf", new[] { "GET", "HEAD" }, async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
        {
            try
            {
                var me = ctx.GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
                if (!await CanAccessUploadAsync(connString, uploadId, me)) return Results.NotFound();

                var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
                if (!File.Exists(path)) return Results.NotFound();
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

    private static async Task<bool> CanAccessUploadAsync(string connString, Guid uploadId, string userId)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($uploadId)
  AND (
        u.UserId = $userId
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $userId
        )
  )
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());
        cmd.Parameters.AddWithValue("$userId", userId);

        return await cmd.ExecuteScalarAsync() is not null;
    }
}
