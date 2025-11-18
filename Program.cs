using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Responses;
using System.Linq;
using OpenAI.Embeddings;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Numerics.Tensors;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using Microsoft.Data.Sqlite;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;



using System.Data;
using Dapper;






// iText7 for page count + raster image counting
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;











static async Task SaveMessageAsync(
    IDbConnection db,
    Guid sessionId,
    string role,           
    string content,
    string? citationsJson, 
    string? pagesJson      
)




{
    const string sql = @"
        INSERT INTO Messages (Id, SessionId, Role, Content, Citations, PagesUsed, CreatedAt)
        VALUES (@Id, @SessionId, @Role, @Content, @Citations, @PagesUsed, @CreatedAt);";

    await db.ExecuteAsync(sql, new
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Role = role,
        Content = content,
        Citations = citationsJson,
        PagesUsed = pagesJson,
        CreatedAt = DateTime.UtcNow
    });
}







var builder = WebApplication.CreateBuilder(args);

// Read OpenAI config (API key + models)
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

// Answer model: big brain for actual answers (default gpt-5.1)
var answerModel = Environment.GetEnvironmentVariable("OPENAI_ANSWER_MODEL")
    ?? "gpt-5.1";

// Classifier model: cheap model for question type classification (default gpt-5-mini)
var classifierModel = Environment.GetEnvironmentVariable("OPENAI_CLASSIFIER_MODEL")
    ?? "gpt-5-mini";

// OpenAI Chat client for answers (we'll also new up a separate client for the classifier later)
builder.Services.AddSingleton<ChatClient>(_ =>
{
    return new ChatClient(model: answerModel, openAiApiKey);
});




// Swagger (optional)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();




builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", p => p
        .WithOrigins("http://localhost:5174", "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});




var app = builder.Build();
app.UseCors("FrontendDev");
// app.UseHttpsRedirection();



var connString = "Data Source=ingestion.db;Cache=Shared";
using (var conn = new SqliteConnection(connString))
{
    conn.Open();

    var dbPath = Path.GetFullPath("ingestion.db");
    Console.WriteLine($"[DB PATH] Using ingestion.db at: {dbPath}");


    // (already inside: using var conn = new SqliteConnection(connString)); conn.Open();

    // 1) Create tables (SQL ONLY here)
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
  Id TEXT PRIMARY KEY,
  Email TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Sessions (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Messages (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SessionId TEXT NOT NULL,
  Role TEXT NOT NULL,
  Content TEXT NOT NULL,
  Citations TEXT NULL,
  PagesUsed TEXT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Notes (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId TEXT NOT NULL,
  SessionId TEXT NULL,
  UploadId TEXT NULL,
  Text TEXT NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Uploads (
  UploadId TEXT PRIMARY KEY,
  UserId   TEXT NOT NULL,
  FilePath TEXT NOT NULL,
  Name TEXT NULL,
  OriginalFileName TEXT NULL,
  CreatedAt TEXT NOT NULL
);


";
    cmd.ExecuteNonQuery();

    // 2) Add FullName column in a SEPARATE command (C# try/catch OUTSIDE SQL)
    try
    {
        using var mig = conn.CreateCommand();
        mig.CommandText = "ALTER TABLE Users ADD COLUMN FullName TEXT NULL";
        mig.ExecuteNonQuery();
    }
    catch
    {
        // Column already exists -> ignore
    }


    // 3) Add Name column to Uploads (safe if already exists)
    try
    {
        using var mig2 = conn.CreateCommand();
        mig2.CommandText = "ALTER TABLE Uploads ADD COLUMN Name TEXT NULL";
        mig2.ExecuteNonQuery();
    }
    catch
    {
        // Column already exists -> ignore
    }


    // 4) Add IsSuperUser flag to Users (0 = normal user, 1 = superuser)
    try
    {
        using var mig3 = conn.CreateCommand();
        mig3.CommandText = "ALTER TABLE Users ADD COLUMN IsSuperUser INTEGER NOT NULL DEFAULT 0";
        mig3.ExecuteNonQuery();
    }
    catch
    {
        // Column already exists -> ignore
    }




    cmd.ExecuteNonQuery();
}


// --- JWT auth gate (protect everything except /ping and /auth/*) ---
var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/ping" };

app.Use(async (ctx, next) =>

{

    // Let CORS handle preflight (do NOT short-circuit)
    if (string.Equals(ctx.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var path = ctx.Request.Path.Value ?? "";
    if (path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase) || openPaths.Contains(path))
    {
        await next();
        return;
    }

    var auth = ctx.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "missing bearer token" });
        return;
    }

    var token = auth.Substring("Bearer ".Length).Trim();
    var secret = Environment.GetEnvironmentVariable("AUTH_JWT_SECRET") ?? "dev-secret-change-me";
    var issuer = Environment.GetEnvironmentVariable("AUTH_JWT_ISSUER") ?? "IngestionApi";
    var audience = Environment.GetEnvironmentVariable("AUTH_JWT_AUDIENCE") ?? "IngestionClient";

    try
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var claims = handler.ValidateToken(
            token,
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            },
            out _
        );

        var userId =
        claims.FindFirst("sub")?.Value ??
        claims.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "invalid token (no sub)" });
            return;
        }

        ctx.Items["userId"] = userId;

        var isSuperClaim = claims.FindFirst("isSuperUser")?.Value;
        bool isSuperUserFlag =
            string.Equals(isSuperClaim, "true", StringComparison.OrdinalIgnoreCase) ||
            isSuperClaim == "1";

        ctx.Items["isSuperUser"] = isSuperUserFlag;

        await next();
    }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "invalid token",
            details = ex.Message
        });
    }

});


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}




// put this near the top of Program.cs, before any app.MapPost(...)




string MapDocTypeToString(DocType t) => t switch
{
    DocType.AcademicResearch => "AcademicResearch",
    DocType.BusinessCase => "BusinessCase",
    DocType.LegalCase => "LegalCase",
    _ => "Other"
};





app.MapGet("/ping", () => Results.Ok("pong"));


// --- Auth: signup (create user) ---
app.MapPost("/auth/signup", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    string email = "", password = "", fullName = "";
    try
    {
        var obj = System.Text.Json.JsonDocument.Parse(body).RootElement;
        if (obj.TryGetProperty("email", out var e)) email = (e.GetString() ?? "").Trim().ToLowerInvariant();
        if (obj.TryGetProperty("password", out var p)) password = p.GetString() ?? "";
        if (obj.TryGetProperty("fullName", out var n)) fullName = (n.GetString() ?? "").Trim();
    }

    catch { }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest(new { error = "email and password required" });

    var userId = Guid.NewGuid().ToString("N");
    var hash = BCrypt.Net.BCrypt.HashPassword(password);

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // Enforce unique email
    var check = conn.CreateCommand();
    check.CommandText = "SELECT 1 FROM Users WHERE Email = $e LIMIT 1";
    check.Parameters.AddWithValue("$e", email);
    var exists = (await check.ExecuteScalarAsync()) != null;
    if (exists) return Results.Conflict(new { error = "email already exists" });

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"INSERT INTO Users (Id, Email, PasswordHash, FullName, CreatedAt)
                    VALUES ($id,$e,$h,$n,$t)";
    cmd.Parameters.AddWithValue("$id", userId);
    cmd.Parameters.AddWithValue("$e", email);
    cmd.Parameters.AddWithValue("$h", hash);
    cmd.Parameters.AddWithValue("$n", string.IsNullOrWhiteSpace(fullName) ? DBNull.Value : fullName);
    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { userId, email, fullName });
});

// --- Auth: login (issue JWT) ---
app.MapPost("/auth/login", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    string email = "", password = "";
    try
    {
        var obj = System.Text.Json.JsonDocument.Parse(body).RootElement;
        if (obj.TryGetProperty("email", out var e)) email = (e.GetString() ?? "").Trim().ToLowerInvariant();
        if (obj.TryGetProperty("password", out var p)) password = p.GetString() ?? "";
    }
    catch { }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest(new { error = "email and password required" });

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    string? userId = null, hash = null, fullName = null;
    bool isSuperUser = false;
    int rawIsSuperUser = -999; // debug

    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, PasswordHash, IFNULL(FullName,''), IFNULL(IsSuperUser,0) FROM Users WHERE Email = $e LIMIT 1";
    cmd.Parameters.AddWithValue("$e", email);
    using (var r = await cmd.ExecuteReaderAsync())
    {
        if (await r.ReadAsync())
        {
            userId = r.GetString(0);
            hash = r.GetString(1);
            fullName = r.GetString(2);

            // 🔍 read raw value from DB
            rawIsSuperUser = r.GetInt32(3);
            isSuperUser = rawIsSuperUser != 0;
        }
    }


    // Hardcode superuser for Prof. Timothy (email check)
    if (string.Equals(email, "timothywong@gmail.com", StringComparison.OrdinalIgnoreCase))
    {
        isSuperUser = true;
    }


    // 🔍 debug print after reading row
    Console.WriteLine($"[LOGIN DEBUG] email={email}, rawIsSuperUser={rawIsSuperUser}, isSuperUserBool={isSuperUser}");



    if (userId is null || hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
        return Results.Unauthorized();

    var secret = Environment.GetEnvironmentVariable("AUTH_JWT_SECRET") ?? "dev-secret-change-me";
    var issuer = Environment.GetEnvironmentVariable("AUTH_JWT_ISSUER") ?? "IngestionApi";
    var audience = Environment.GetEnvironmentVariable("AUTH_JWT_AUDIENCE") ?? "IngestionClient";

    var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
    var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new System.Security.Claims.Claim("sub", userId),
        new System.Security.Claims.Claim("email", email),
        new System.Security.Claims.Claim("isSuperUser", isSuperUser ? "true" : "false"),
    };

    var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddMinutes(60),
        signingCredentials: creds
    );

    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = jwt, userId, email, fullName, isSuperUser });
});





// POST /uploads  (save PDF + minimal summary) — uses ABSOLUTE uploads path
app.MapPost("/uploads", async (HttpRequest request, HttpContext ctx, IWebHostEnvironment env) =>
{
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

    int images = PdfImageUtils.CountRasterImagesExact(filePath);

    var summary = new
    {
        uploadId,
        fileName = file.FileName,
        fileSizeBytes,
        fileSizeMB,
        pages,
        counts = new { images },
        uploadedAt = uploadedAt.ToString("o"),
        generatedAt = DateTime.UtcNow.ToString("o")
    };

    var summaryPath = Path.Combine(uploadsRoot, $"{uploadId}.summary.json");
    await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary));

    // Use the original filename from the upload (e.g. "Healthcare Case.pdf")
    var originalFileName = Path.GetFileName(file.FileName);


    // persist ownership (per-user scoping)
    var ownerId = (string?)ctx.Items["userId"] ?? "";
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared"))
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
app.MapGet("/uploads/{uploadId:guid}/summary", async (Guid uploadId, IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.summary.json");
    if (!File.Exists(path)) return Results.NotFound();
    var json = await File.ReadAllTextAsync(path);
    return Results.Text(json, "application/json");
});

// GET /cases — scans ABSOLUTE uploads folder
app.MapGet("/cases", (IWebHostEnvironment env) =>
{
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
app.MapMethods("/uploads/{uploadId:guid}.pdf", new[] { "GET", "HEAD" }, (Guid uploadId, IWebHostEnvironment env) =>
{
    try
    {
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


// DEV: simple SSE mock stream
app.MapGet("/api/chat/stream", async (HttpContext ctx) =>
{
    // Required SSE headers
    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-transform";
    ctx.Response.Headers["Connection"] = "keep-alive";

    // Kick the stream so proxies don’t buffer forever
    await ctx.Response.WriteAsync("\n");
    await ctx.Response.Body.FlushAsync();

    var prompt = ctx.Request.Query["prompt"].ToString();

    var text =
        $"Thanks! I looked at your prompt ({(string.IsNullOrWhiteSpace(prompt) ? "…" : prompt)}) " +
        "and found key evidence on page 5. Here’s a quick summary to get you started.";

    var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var delay = TimeSpan.FromMilliseconds(50);

    try
    {
        foreach (var t in tokens)
        {
            if (ctx.RequestAborted.IsCancellationRequested) break;

            // stream one token
            await ctx.Response.WriteAsync($"event: token\ndata: {{\"text\":\"{t}\"}}\n\n");
            await ctx.Response.Body.FlushAsync();

            await Task.Delay(delay, ctx.RequestAborted);
        }

        if (!ctx.RequestAborted.IsCancellationRequested)
        {
            // one source chip (page 5), then done
            await ctx.Response.WriteAsync("event: source\ndata: {\"page\":5,\"label\":\"p. 5\"}\n\n");
            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // client disconnected—ignore
    }
});

// Figures/visuals for a document (MVP: stub data)
// GET /api/documents/{caseId}/figures
app.MapGet("/api/documents/{caseId}/figures", (string caseId) =>
{
    // TODO: Replace this stub with your real analysis lookup for `caseId`
    // Shape: [{ id, page, type:"image", caption, bbox:null }]
    var stub = new[]
    {
        new { id = $"{caseId}-p3-1",  page = 3,  type = "image", caption = "Visual on page 3",  bbox = (object?)null },
        new { id = $"{caseId}-p7-1",  page = 7,  type = "image", caption = "Visual on page 7",  bbox = (object?)null },
        new { id = $"{caseId}-p10-1", page = 10, type = "image", caption = "Visual on page 10", bbox = (object?)null },
    };

    return Results.Json(stub);
});


// GET /api/llm/ping — round-trip to model
app.MapGet("/api/llm/ping", async (OpenAI.Chat.ChatClient chat) =>
{
    // Some installs return ClientResult<ChatCompletion>; take .Value to get ChatCompletion
    var result = await chat.CompleteChatAsync("Reply exactly: hello from CasePilot Q&A");
    var completion = result.Value;               // <-- the key fix
    var text = completion.Content.Count > 0
        ? completion.Content[0].Text ?? ""
        : "";

    return Results.Json(new { ok = text.Contains("hello from CasePilot Q&A"), reply = text });
});

// GET /api/embeddings/ping — sanity check: returns vector length
// GET /api/embeddings/ping — sanity check: returns vector length
app.MapGet("/api/embeddings/ping", () =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    var client = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);

    var result = client.GenerateEmbedding("hello world");  // wrapper + value
    var dims = result.Value.ToFloats().Length;            // <-- unwrap, then ToFloats()

    return Results.Json(new { dims });
});

// ---- text extraction helper (PdfPig) ----
static IEnumerable<(int page, string text)> ExtractPerPageText(string path)
{
    using var doc = UglyToad.PdfPig.PdfDocument.Open(path); // PdfPig
    foreach (var page in doc.GetPages())
    {
        var txt = page.Text ?? string.Empty; // plain text per page
        yield return (page.Number, txt);
    }
}

// GET /uploads/{id}/pages/preview  -> returns first few page snippets (no embeddings yet)
app.MapGet("/uploads/{uploadId:guid}/pages/preview", (Guid uploadId, IWebHostEnvironment env) =>
{
    var pdfPath = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
    if (!System.IO.File.Exists(pdfPath)) return Results.NotFound();

    var preview = ExtractPerPageText(pdfPath)
        .Take(3)
        .Select(p => new
        {
            page = p.page,
            snippet = SafeHead(p.text, 300) + (p.text.Length > 300 ? "…" : "")
        });

    return Results.Json(preview);
});


// ---- simple in-memory vector index ----

// POST /index/{uploadId} — embed per-page text into an in-memory index (and persist to disk)
app.MapPost("/index/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
{

    // ownership check
    var me = (string?)ctx.Items["userId"] ?? "";
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared"))
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT 1 FROM Uploads WHERE UploadId = $u AND UserId = $me LIMIT 1";
        chk.Parameters.AddWithValue("$u", uploadId);
        chk.Parameters.AddWithValue("$me", me);
        var ok = await chk.ExecuteScalarAsync();
        if (ok is null)
            return Results.NotFound(new { error = "not found" }); // don't leak existence
    }



    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var pdfPath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");
    if (!System.IO.File.Exists(pdfPath))
        return Results.NotFound(new { error = "PDF not found" });

    var emb = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
    var chunks = new List<IndexedChunk>();
    int pagesIndexed = 0;

    using (var pdf = PdfPigDoc.Open(pdfPath))
    {
        foreach (var page in pdf.GetPages())
        {
            var raw = (page.Text ?? "").Trim();
            var text = TextNormalization.Clean(raw);

            if (string.IsNullOrWhiteSpace(text)) continue;
            pagesIndexed++;

            foreach (var c in TextChunking.ChunkBySize(text, 1200, 200)) // larger, still safe
            {
                var vec = emb.GenerateEmbedding(c).Value.ToFloats();
                var preview = c; // keep full chunk text (no truncation)
                chunks.Add(new IndexedChunk(page.Number, vec, preview));
            }

        }
    }

    // store in memory
    InMemoryStore.VectorIndex[uploadId.ToString()] = chunks;


    try
    {
        var cls = DocTypeClassifier.Evaluate(chunks);
        DocTypePersistence.Save(uploadId, env, cls);
        Console.WriteLine($"[CLASSIFY] {uploadId} -> {cls.DocType} (conf {cls.Confidence:0.00}) :: {cls.Reason}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CLASSIFY ERROR] {ex.Message}");
    }
    // persist to disk
    Directory.CreateDirectory(uploadsRoot);
    var serializable = chunks.Select(c => new SerializableChunk(c.Page, c.Preview, c.Vec.ToArray())).ToArray();
    var indexPath = Path.Combine(uploadsRoot, $"{uploadId}.index.json");
    await System.IO.File.WriteAllTextAsync(indexPath, System.Text.Json.JsonSerializer.Serialize(serializable));

    return Results.Json(new
    {
        uploadId,
        chunks = chunks.Count,
        pagesIndexed,
        sample = chunks.Take(3).Select(x => new { page = x.Page, preview = x.Preview })
    });
});

// GET /uploads/{uploadId}/classification -> returns doc type & confidence
app.MapGet("/uploads/{uploadId:guid}/classification", (Guid uploadId, IWebHostEnvironment env) =>
{
    if (DocTypePersistence.TryLoad(uploadId, env, out var cls) && cls != null)
        return Results.Json(cls);

    return Results.NotFound(new { error = "No classification stored for this uploadId." });
});




// GET /search/{uploadId}?q=...  -> top-k chunks by cosine similarity
app.MapGet("/search/{uploadId:guid}", (Guid uploadId, string q, IWebHostEnvironment env) =>
{
    // Lazy-load index from disk if missing in RAM
    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
    {
        if (!IndexPersistence.TryLoad(uploadId, env, out list))
            return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });
    }

    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
    var qVec = embClient.GenerateEmbedding(q ?? string.Empty).Value.ToFloats();

    var scored = list
        .Select(x => new
        {
            x.Page,
            x.Preview,
            score = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(qVec.Span, x.Vec.Span)
        })
        .OrderByDescending(s => s.score)
        .Take(5)
        .ToList();

    return Results.Json(scored);
});
// GET /ask/{uploadId}?q=...
app.MapGet("/ask/{uploadId:guid}", async (Guid uploadId, string q, string? sessionId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared"))
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT 1 FROM Uploads WHERE UploadId = $u AND UserId = $me LIMIT 1";
        chk.Parameters.AddWithValue("$u", uploadId);
        chk.Parameters.AddWithValue("$me", me);
        var ok = await chk.ExecuteScalarAsync();
        if (ok is null) return Results.NotFound(new { error = "not found" });
    }

    // Keep the original question and classify it with the small model
    var questionOriginal = q ?? string.Empty;

    // High-level classification: Summary / Fact / Method / Findings / WhyExplain / Other
    var questionType = await ClassifyQuestionAsync(questionOriginal);



    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
    {
        if (!IndexPersistence.TryLoad(uploadId, env, out list))
            return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });
    }

    // --- local helper to persist a message for this session (if any) ---
    void SaveMessage(string role, string content, int[]? citations, int[]? pages)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        using var mconn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
        mconn.Open();
        using var mcmd = mconn.CreateCommand();
        mcmd.CommandText = @"
            INSERT INTO Messages (SessionId, Role, Content, Citations, PagesUsed, CreatedAt)
            VALUES ($sid, $role, $content, $cites, $pages, $ts)";
        mcmd.Parameters.AddWithValue("$sid", sessionId);
        mcmd.Parameters.AddWithValue("$role", role);
        mcmd.Parameters.AddWithValue("$content", content);

        if (citations != null && citations.Length > 0)
            mcmd.Parameters.AddWithValue("$cites", System.Text.Json.JsonSerializer.Serialize(citations.Distinct()));
        else
            mcmd.Parameters.AddWithValue("$cites", DBNull.Value);

        if (pages != null && pages.Length > 0)
            mcmd.Parameters.AddWithValue("$pages", System.Text.Json.JsonSerializer.Serialize(pages.Distinct()));
        else
            mcmd.Parameters.AddWithValue("$pages", DBNull.Value);

        mcmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
        mcmd.ExecuteNonQuery();
    }

    try
    {
        // --- record USER message (if a session was provided) ---
        SaveMessage("user", q, null, null);

        // ---- Normalize & detect intent/category
        var qNorm = QueryNormalization.Normalize(q ?? "");

        // map “title of this/the document/pdf” → “document title”
        qNorm = Regex.Replace(qNorm, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                              "document title", RegexOptions.IgnoreCase);
        // map author-like phrasings → "authors"
        qNorm = Regex.Replace(qNorm, @"\b(authors?|students?|contributors?|prepared\s+by|by\s+whom)\b",
                              "authors", RegexOptions.IgnoreCase);
        // map “findings/takeaways/insights/conclude” → conclusion
        qNorm = Regex.Replace(qNorm, @"\b(key\s+findings?|findings?|key\s+takeaways?|takeaways?|insights?|what\s+did\s+they\s+conclude|conclusions?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “results/outcomes/observations/measurements” → conclusion (closest existing intent)
        qNorm = Regex.Replace(qNorm, @"\b(results?|experimental\s+results?|outcomes?|observations?|measurements?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “summary/overview/tldr/summarize/in N bullets” → abstract
        qNorm = Regex.Replace(qNorm, @"\b(abstract|summary|overview|tl;dr|summari[sz]e|in\s+\d+\s+bullets?)\b",
                              "abstract", RegexOptions.IgnoreCase);

        var intent = SectionSwitchboard.Detect(qNorm);
        var cat = CategoryDetector.Detect(qNorm);

        // --- bullet-count “askQ” for abstract requests ---
        string askQ = qNorm;
        if (intent == SectionIntent.Abstract)
        {
            var m = Regex.Match(
                q ?? "",
                @"\b(?:in\s+)?(?:(?<num>\d{1,2})|(?<word>one|two|three|four|five|six|seven|eight|nine|ten))\s+(?:bullets?|points?|items?)\b",
                RegexOptions.IgnoreCase
            );

            int count = 5;
            if (m.Success)
            {
                count = m.Groups["num"].Success
                    ? int.Parse(m.Groups["num"].Value)
                    : WordToInt(m.Groups["word"].Value);
            }
            count = Math.Min(10, Math.Max(3, count)); // clamp 3..10

            askQ =
                $"{qNorm}\n\n" +
                $"Return exactly {count} bullet points, numbered 1..{count}, one per line. " +
                $"No intro or outro. Each bullet MUST end with a [p:X] chip. " +
                $"Do not produce fewer than {count} bullets.";
        }

        var catHint = cat.PromptHint;
        var techGroup = string.Equals(cat.Name, "tech_group", StringComparison.OrdinalIgnoreCase);

        // ==== FAST PATH: Authors (front matter only) ====
        if (intent == SectionIntent.Authors)
        {
            var (_, metaAuthor) = PdfMetadataHelper.Read(uploadId, env);

            // 2) restrict to pages 1–2 (fallback 1–4), excluding References/Bibliography
            var front = list.Where(x => x.Page >= 1 && x.Page <= 2).ToList();
            if (front.Count == 0) front = list.Where(x => x.Page >= 1 && x.Page <= 4).ToList();
            front = front.Where(x => !Regex.IsMatch(x.Preview, @"\b(References|Bibliography|Works Cited)\b",
                                                    RegexOptions.IgnoreCase)).ToList();

            // prefer anchors
            var anchors = new Regex(@"\b(By|Authors?|Name of Student|Group Members|Prepared by)\b", RegexOptions.IgnoreCase);
            var prioritized = front.OrderByDescending(x => anchors.IsMatch(x.Preview) ? 1 : 0)
                                   .Take(12).ToList();

            if (prioritized.Count > 0)
            {
                var ctxOnlyFront = string.Join("\n\n", prioritized.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
                var pagesOnlyFront = prioritized.Select(t => t.Page).Distinct().ToArray();
                return AnswerWithContext(ctxOnlyFront, qNorm, pagesOnlyFront, apiKey, catHint, SaveMessage);
            }
            // if nothing found, fall through to normal flow
        }

        // ---- Embed the normalized query
        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
        var qVec = embClient.GenerateEmbedding(qNorm).Value.ToFloats();

        // ---- Retrieval
        var top = QaRetrieval.SelectTop(list, qVec.Span, qNorm, forStreaming: false);

        // ---- Section switchboard (Title handled later)
        if (intent != SectionIntent.None && intent != SectionIntent.Title && intent != SectionIntent.Authors)
        {
            List<TopChunk> secHits;
            if (intent == SectionIntent.Abstract)
            {
                var a = SectionSwitchboard.FindSection(list, SectionIntent.Abstract);
                var c = SectionSwitchboard.FindSection(list, SectionIntent.Conclusion);
                secHits = a.Concat(c).ToList(); // Abstract + Conclusion
            }
            else
            {
                secHits = SectionSwitchboard.FindSection(list, intent);
            }

            if (secHits.Count > 0)
            {
                var stitchedSec = ContextStitching.ExpandWithNeighbors(list, secHits, sideNeighbors: 2, maxTotalNeighbors: 8);
                var ctxSec = string.Join("\n\n", stitchedSec.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
                return AnswerWithContext(ctxSec, askQ, stitchedSec.Select(t => t.Page).Distinct().ToArray(), apiKey, catHint, SaveMessage);
            }
        }

        // ---- Auto-escalate for listy queries if thin
        // ---- Option B: Adaptive breadth (vague/global queries get wider K)
        // ---- Auto-escalate for listy queries if thin
        // Use QuestionType + intent to shape breadth
        var isSummary = questionType == QuestionType.Summary;
        var isFact = questionType == QuestionType.Fact;
        var isMethod = questionType == QuestionType.Method;
        var isFindings = questionType == QuestionType.Findings;
        var isWhyExplain = questionType == QuestionType.WhyExplain;

        // ---- Option B: Adaptive breadth (vague/global queries get wider K)
        bool vague = isSummary
            || isFindings
            || isWhyExplain
            || QaRetrieval.IsListy(qNorm)
            || intent == SectionIntent.Abstract
            || intent == SectionIntent.Conclusion
            || Regex.IsMatch(qNorm, @"\b(key\s+findings?|takeaways?|insights?|overview|summary|summari[sz]e|tl;dr)\b",
                             RegexOptions.IgnoreCase);

        int desiredK;
        if (isSummary)
            desiredK = 20;         // very broad context
        else if (isFindings)
            desiredK = 14;         // results-type questions: moderately broad
        else if (isMethod)
            desiredK = 10;         // methods usually need a bit more spread
        else if (isFact)
            desiredK = 6;          // narrow, precise
        else if (vague)
            desiredK = 12;         // generic vague / listy
        else
            desiredK = 8;          // default

        if (top.Count < desiredK)
        {
            var fbWider = QaRetrieval.KeywordFallback(list, qNorm, k: desiredK);
            if (fbWider.Count > top.Count)
            {
                top = fbWider;
            }
        }

        var lowIntent = intent is SectionIntent.Abstract or SectionIntent.Introduction
              or SectionIntent.Conclusion or SectionIntent.References
              or SectionIntent.Keywords or SectionIntent.Authors
              or SectionIntent.Title;

        // Base threshold: Title stays strict; Facts get stricter; Summary can be looser.
        var THRESHOLD = intent == SectionIntent.Title
            ? 0.99f
            : isFact
                ? 0.15f
                : (lowIntent || isSummary ? 0.00f : 0.10f);


        var bestScore = top.Count > 0 ? top.Max(t => t.Score) : 0f;
        var pageSpread = top.Select(t => t.Page).Distinct().Count();

        if (bestScore < THRESHOLD && pageSpread >= 3)
        {
            THRESHOLD = Math.Max(0.05f, THRESHOLD - 0.05f);
        }

        if (top.Count == 0 || bestScore < THRESHOLD)
        {
            // Title/Authors metadata first; then Title heuristics
            if (intent == SectionIntent.Title || intent == SectionIntent.Authors)
            {
                var (metaTitle, metaAuthor) = PdfMetadataHelper.Read(uploadId, env);

                if (intent == SectionIntent.Title && !string.IsNullOrWhiteSpace(metaTitle) &&
                    !Regex.IsMatch(metaTitle, @"^\s*untitled\s*$", RegexOptions.IgnoreCase))
                {
                    var answerText = $"From PDF metadata: {metaTitle}";
                    SaveMessage("assistant", answerText, null, null);

                    return Results.Json(new
                    {
                        answer = answerText,
                        pagesUsed = Array.Empty<int>(),
                        citations = Array.Empty<int>()
                    });
                }

                if (intent == SectionIntent.Authors && false && !string.IsNullOrWhiteSpace(metaAuthor) &&
                    !Regex.IsMatch(metaAuthor, @"^\s*(unknown|n/?a|none)\s*$", RegexOptions.IgnoreCase))
                {
                    var answerText = $"From PDF metadata: {metaAuthor}";
                    SaveMessage("assistant", answerText, null, null);

                    return Results.Json(new
                    {
                        answer = answerText,
                        pagesUsed = Array.Empty<int>(),
                        citations = Array.Empty<int>()
                    });
                }

                if (intent == SectionIntent.Title)
                {
                    var guess = TitleHeuristics.FromPdfFirstPage(uploadId, env);
                    if (!string.IsNullOrWhiteSpace(guess))
                    {
                        var pagesGuess = new[] { 1 };
                        SaveMessage("assistant", guess, null, pagesGuess);

                        return Results.Json(new
                        {
                            answer = guess,
                            pagesUsed = pagesGuess,
                            citations = Array.Empty<int>() // no chunk ids in this path
                        });
                    }
                }
            }

            // Generic lexical fallback
            // Generic lexical fallback
            var fb = QaRetrieval.KeywordFallback(list, qNorm, k: 8);
            if (fb.Count == 0)
            {
                var answerText =
                    "I can't find that in the document. " +
                    "Based on the indexed text and search, there doesn't seem to be a clear, explicit answer to this question.";
                SaveMessage("assistant", answerText, null, null);

                return Results.Json(new
                {
                    answer = answerText,
                    citations = Array.Empty<int>(),
                    pagesUsed = Array.Empty<int>(),
                    retrieval = new { bestScore, threshold = THRESHOLD }
                });
            }


            var stitchedFb = ContextStitching.ExpandWithNeighbors(list, fb,
                sideNeighbors: techGroup ? 2 : 1,
                maxTotalNeighbors: techGroup ? 10 : 6);
            var ctxStrFb = string.Join("\n\n", stitchedFb.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            return AnswerWithContext(ctxStrFb, askQ, stitchedFb.Select(t => t.Page).Distinct().ToArray(), apiKey, catHint, SaveMessage);
        }

        // ---- Normal path
        var stitchedTop = ContextStitching.ExpandWithNeighbors(list, top,
            sideNeighbors: techGroup ? 2 : 1,
            maxTotalNeighbors: techGroup ? 10 : 6);
        var ctxStr = string.Join("\n\n", stitchedTop.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
        return AnswerWithContext(ctxStr, askQ, stitchedTop.Select(t => t.Page).Distinct().ToArray(), apiKey, catHint, SaveMessage);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ASK ERROR] {ex.GetType().Name}: {ex.Message}");
        return Results.Json(new { error = ex.GetType().Name, message = ex.Message });
    }

    // Local helper (non-static so it can capture SaveMessage)
    IResult AnswerWithContext(string ctxStr, string question, int[] pages, string apiKeyLocal, string categoryHint, Action<string, string, int[]?, int[]?> saveMessage)
    {
        var chat = new OpenAI.Chat.ChatClient(model: answerModel, apiKeyLocal);

        // Extra hint to steer behavior based on the classifier
        var questionTypeHint = questionType switch
        {
            QuestionType.Summary =>
                "The user is asking for a high-level overview/summary of the document. Focus on the big picture.",
            QuestionType.Fact =>
                "The user is asking for specific factual details. Extract precise facts directly from the Context.",
            QuestionType.Method =>
                "The user is asking about the study's methodology, data collection, or procedures. Focus on how the work was done.",
            QuestionType.Findings =>
                "The user is asking about the main findings or results. Focus on outcomes, measurements, and key results.",
            QuestionType.WhyExplain =>
                "The user is asking for an explanation, rationale, or interpretation. Explain the reasoning using the Context.",
            _ =>
                string.Empty
        };

        // Decide if the user is explicitly asking for bullets / a list
        var requiresBullets = Regex.IsMatch(
            question ?? string.Empty,
            @"\b(bullets?|points?|list)\b",
            RegexOptions.IgnoreCase
        );

        var bulletRules = requiresBullets
            ? "If the user asks for bullets or a list, respond with clear numbered bullet points. " +
              "Every bullet line MUST end with a [p:X] chip. " +
              "If the user asks to summarize in N bullets, aim for exactly N bullet points numbered 1..N. " +
              "If the content clearly does not support N distinct points, it is acceptable to provide fewer and explicitly say so. "
            : "Use natural sentences or short paragraphs when appropriate. " +
              "You may use a short bullet list only if it improves clarity. " +
              "Regardless of format, any specific factual claim must end with the relevant [p:X] chip. ";

        var sys =
            "You are a precise assistant. Answer ONLY using the provided Context. " +
            "If the answer is not in Context, reply: I can't find that in the document. " +
            "When listing, include all items that are clearly supported by the Context; do not guess beyond it. " +
            "When the user asks to list items of a specific category, focus on items that match that category, " +
            "and clearly label any closely related items if you include them. " +
            (string.IsNullOrWhiteSpace(categoryHint) ? "" : categoryHint + " ") +
            (string.IsNullOrWhiteSpace(questionTypeHint) ? "" : questionTypeHint + " ") +
            bulletRules;


        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.SystemChatMessage(sys),
            new OpenAI.Chat.UserChatMessage($@"Question: {question}

Context:
{ctxStr}")
        };

        var result = chat.CompleteChat(messages).Value;
        var answer = string.Concat(result.Content.Select(part => part.Text ?? string.Empty)).Trim();

        if (string.IsNullOrWhiteSpace(answer))
        {
            var answerText =
                "I can't find that in the document. " +
                "The model could not extract a grounded answer from the provided context.";
            saveMessage("assistant", answerText, null, pages);

            return Results.Json(new
            {
                answer = answerText,
                pagesUsed = pages,
                citations = Array.Empty<int>(),
                debug = new
                {
                    note = "Empty model reply; joined all parts",
                    contextPreview = ctxStr.Length > 300 ? ctxStr[..300] + "…" : ctxStr
                }
            });
        }

        var citations = Regex
            .Matches(answer, @"\[\s*p\s*:\s*(\d+)\s*\]", RegexOptions.IgnoreCase)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToArray();

        saveMessage("assistant", answer, citations, pages);

        return Results.Json(new { answer, pagesUsed = pages, citations });
    }
});


// GET /ask/stream/{uploadId}?q=...  -> SSE: token-by-token answer + citations + done
app.MapGet("/ask/stream/{uploadId:guid}", async (Guid uploadId, string q, string? sessionId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared"))
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT 1 FROM Uploads WHERE UploadId = $u AND UserId = $me LIMIT 1";
        chk.Parameters.AddWithValue("$u", uploadId); chk.Parameters.AddWithValue("$me", me);
        var ok = await chk.ExecuteScalarAsync();
        if (ok is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound; // don’t leak existence
            await ctx.Response.WriteAsJsonAsync(new { error = "not found" });
            return; // IMPORTANT in SSE handlers
        }
    }
    // Keep the original question and classify it with the small model
    var questionOriginal = q ?? string.Empty;

    // High-level classification: Summary / Fact / Method / Findings / WhyExplain / Other
    var questionType = await ClassifyQuestionAsync(questionOriginal);


    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"] = "keep-alive";
    await ctx.Response.WriteAsync("\n");
    await ctx.Response.Body.FlushAsync();

    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
    {
        if (!IndexPersistence.TryLoad(uploadId, env, out list))
        {
            await ctx.Response.WriteAsync("event: error\ndata: {\"message\":\"Not indexed. POST /index first.\"}\n\n");
            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
            return;
        }
    }

    // --- local helper to persist a message for this session (if any) ---
    void SaveMessage(string role, string content, int[]? citations, int[]? pages)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        using var mconn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
        mconn.Open();
        using var mcmd = mconn.CreateCommand();
        mcmd.CommandText = @"
            INSERT INTO Messages (SessionId, Role, Content, Citations, PagesUsed, CreatedAt)
            VALUES ($sid, $role, $content, $cites, $pages, $ts)";
        mcmd.Parameters.AddWithValue("$sid", sessionId);
        mcmd.Parameters.AddWithValue("$role", role);
        mcmd.Parameters.AddWithValue("$content", content);

        if (citations != null && citations.Length > 0)
            mcmd.Parameters.AddWithValue("$cites", System.Text.Json.JsonSerializer.Serialize(citations.Distinct()));
        else
            mcmd.Parameters.AddWithValue("$cites", DBNull.Value);

        if (pages != null && pages.Length > 0)
            mcmd.Parameters.AddWithValue("$pages", System.Text.Json.JsonSerializer.Serialize(pages.Distinct()));
        else
            mcmd.Parameters.AddWithValue("$pages", DBNull.Value);

        mcmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
        mcmd.ExecuteNonQuery();
    }



    try
    {
        // --- record USER message at the start of the main happy path ---
        SaveMessage("user", q, null, null);

        // Normalize + shims
        var qNorm = QueryNormalization.Normalize(q ?? "");
        // map “title of this/the document/pdf” → “document title”
        qNorm = Regex.Replace(qNorm, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                              "document title", RegexOptions.IgnoreCase);
        // map author-like phrasings → "authors"
        qNorm = Regex.Replace(qNorm, @"\b(authors?|students?|contributors?|prepared\s+by|by\s+whom)\b",
                              "authors", RegexOptions.IgnoreCase);
        // map “findings/takeaways/insights/conclude” → conclusion
        qNorm = Regex.Replace(qNorm, @"\b(key\s+findings?|findings?|key\s+takeaways?|takeaways?|insights?|what\s+did\s+they\s+conclude|conclusions?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “results/outcomes/observations/measurements” → conclusion
        qNorm = Regex.Replace(qNorm, @"\b(results?|experimental\s+results?|outcomes?|observations?|measurements?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “summary/overview/tldr/summarize/in N bullets” → abstract
        qNorm = Regex.Replace(qNorm, @"\b(abstract|summary|overview|tl;dr|summari[sz]e|in\s+\d+\s+bullets?)\b",
                              "abstract", RegexOptions.IgnoreCase);

        var intent = SectionSwitchboard.Detect(qNorm);
        var cat = CategoryDetector.Detect(qNorm);
        var catHint = cat.PromptHint;
        var techGroup = string.Equals(cat.Name, "tech_group", StringComparison.OrdinalIgnoreCase);

        // --- D1+: bullet formatting hint for Abstract intent ---
        string summaryHint = "";
        if (intent == SectionIntent.Abstract)
        {
            var m = Regex.Match(
                q ?? "",
                @"\b(?:in\s+)?(?:(?<num>\d{1,2})|(?<word>one|two|three|four|five|six|seven|eight|nine|ten))\s+(?:bullets?|points?|items?)\b",
                RegexOptions.IgnoreCase
            );

            int count = 5;
            if (m.Success)
            {
                count = m.Groups["num"].Success
                    ? int.Parse(m.Groups["num"].Value)
                    : WordToInt(m.Groups["word"].Value);
            }
            count = Math.Min(10, Math.Max(3, count)); // clamp 3..10

            summaryHint =
                $"Return exactly {count} bullet points, numbered 1..{count}, one per line. " +
                $"No intro or outro. Each bullet MUST end with a [p:X] chip. " +
                $"Do not produce fewer than {count} bullets. " +
                $"Use evidence from all provided section pages (Abstract and Conclusion if present); " +
                $"spread citations across them when relevant.";
        }

        // ==== FAST PATH: Title ====
        if (intent == SectionIntent.Title)
        {
            var (metaTitle, _) = PdfMetadataHelper.Read(uploadId, env);
            if (!string.IsNullOrWhiteSpace(metaTitle) &&
                !Regex.IsMatch(metaTitle, @"^\s*untitled\s*$", RegexOptions.IgnoreCase))
            {
                await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = $"From PDF metadata: {metaTitle}" })}\n\n");
                await ctx.Response.WriteAsync("event: citations\ndata: []\n\n");

                var answerText = $"From PDF metadata: {metaTitle}";
                SaveMessage("assistant", answerText, null, null);

                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }

            var guess = TitleHeuristics.FromPdfFirstPage(uploadId, env);
            if (!string.IsNullOrWhiteSpace(guess))
            {
                await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = $"{guess} [p:1]" })}\n\n");
                await ctx.Response.WriteAsync("event: citations\ndata: [1]\n\n");

                // assistant placeholder for this streamed response
                var answerText = $"{guess} [p:1]";
                SaveMessage("assistant", answerText, new[] { 1 }, new[] { 1 });

                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }
            // fall through if nothing found
        }

        // ==== FAST PATH: Authors (front matter only) ====
        if (intent == SectionIntent.Authors)
        {
            var (_, metaAuthor) = PdfMetadataHelper.Read(uploadId, env);
            if (false && !string.IsNullOrWhiteSpace(metaAuthor) &&
                !Regex.IsMatch(metaAuthor, @"^\s*(unknown|n/?a|none)\s*$", RegexOptions.IgnoreCase))
            {
                await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = metaAuthor })}\n\n");
                await ctx.Response.WriteAsync("event: citations\ndata: []\n\n");

                SaveMessage("assistant", metaAuthor, null, null);


                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }

            var front = list.Where(x => x.Page >= 1 && x.Page <= 2).ToList();
            if (front.Count == 0) front = list.Where(x => x.Page >= 1 && x.Page <= 4).ToList();
            front = front.Where(x => !Regex.IsMatch(x.Preview, @"\b(References|Bibliography|Works Cited)\b",
                                                    RegexOptions.IgnoreCase)).ToList();

            var anchors = new Regex(@"\b(By|Authors?|Name of Student|Group Members|Prepared by)\b", RegexOptions.IgnoreCase);
            var prioritized = front.OrderByDescending(x => anchors.IsMatch(x.Preview) ? 1 : 0)
                                   .Take(12).ToList();

            var ctxStr = string.Join("\n\n", prioritized.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            var pages = prioritized.Select(t => t.Page).Distinct().ToArray();

            var chatFast = new OpenAI.Chat.ChatClient(model: answerModel, apiKey);

            var promptFast = $"""
You are a precise assistant. Answer ONLY using the Context below.
If the answer is not in Context, say: "I can't find that in the document."
When listing, include ALL items you find in Context; don't guess.
Include ONLY items that strictly match the requested category (authors/students); exclude references, citations, and bibliographic entries.
Every bullet line MUST end with a [p:X] chip.
If the user asks to summarize in N bullets, you must return exactly N bullet points numbered 1..N; do not produce fewer than N bullets even if content seems limited—split broader points as needed.
{(string.IsNullOrWhiteSpace(summaryHint) ? "" : summaryHint + "\n")}

Question: {qNorm}

Context:
{ctxStr}
""";

            var updatesFast = chatFast.CompleteChatStreaming(promptFast);
            var sbFast = new System.Text.StringBuilder();

            foreach (var update in updatesFast)
            {
                if (ctx.RequestAborted.IsCancellationRequested) break;
                if (update.ContentUpdate.Count > 0)
                {
                    var piece = update.ContentUpdate[0].Text ?? "";
                    sbFast.Append(piece);
                    await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = piece })}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            }

            var answerFast = sbFast.ToString();

            // Extract cited pages from the model output
            var rawCites = Regex.Matches(answerFast, @"\[\s*p\s*:\s*(\d+)\s*\]", RegexOptions.IgnoreCase)
                                .Select(m => int.Parse(m.Groups[1].Value))
                                .Distinct()
                                .ToArray();

            // Only keep citations that exist in the retrieved pages
            var allowed = new HashSet<int>(pages);
            var filteredCites = rawCites.Where(p => allowed.Contains(p)).ToArray();

            // If model gave nothing valid, fall back to the retrieved pages (not random numbers)
            var finalCites = filteredCites.Length > 0 ? filteredCites : pages;

            await ctx.Response.WriteAsync(
                $"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(finalCites)}\n\n");

            var answerText = answerFast;
            SaveMessage("assistant", answerText, finalCites, finalCites);

            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
            return;
        }

        // ==== normal retrieval (streamed) ====
        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
        var qVec = embClient.GenerateEmbedding(qNorm).Value.ToFloats();

        var top = QaRetrieval.SelectTop(list, qVec.Span, qNorm, forStreaming: true);

        // Try section switchboard first (Abstract includes Conclusion = A+)
        string? context = null;
        if (intent != SectionIntent.None && intent != SectionIntent.Title && intent != SectionIntent.Authors)
        {
            List<TopChunk> secHits;
            if (intent == SectionIntent.Abstract)
            {
                var a = SectionSwitchboard.FindSection(list, SectionIntent.Abstract);
                var c = SectionSwitchboard.FindSection(list, SectionIntent.Conclusion);
                secHits = a.Concat(c).ToList(); // Abstract + Conclusion
            }
            else
            {
                secHits = SectionSwitchboard.FindSection(list, intent);
            }

            if (secHits.Count > 0)
            {
                var stitchedSec = ContextStitching.ExpandWithNeighbors(list, secHits, sideNeighbors: 2, maxTotalNeighbors: 8);
                context = string.Join("\n\n", stitchedSec.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            }
        }

        // Option B: Adaptive breadth + gentler threshold
        // (only applies if we didn't already build section context)
        if (context == null)
        {
            bool vague = QaRetrieval.IsListy(qNorm)
                || intent == SectionIntent.Abstract
                || intent == SectionIntent.Conclusion
                || Regex.IsMatch(qNorm, @"\b(key\s+findings?|takeaways?|insights?|overview|summary|summari[sz]e|tl;dr)\b",
                                 RegexOptions.IgnoreCase);

            int desiredK = vague ? 12 : 8;
            if (top.Count < desiredK)
            {
                var fbWider = QaRetrieval.KeywordFallback(list, qNorm, k: desiredK);
                if (fbWider.Count > top.Count) top = fbWider;
            }

            // threshold after widening (so it reflects current 'top')
            var lowIntent = intent is SectionIntent.Abstract or SectionIntent.Introduction
                          or SectionIntent.Conclusion or SectionIntent.References
                          or SectionIntent.Keywords or SectionIntent.Authors
                          or SectionIntent.Title;

            var THRESHOLD = intent == SectionIntent.Title ? 0.99f : (lowIntent ? 0.00f : 0.10f);

            var bestScore = top.Count > 0 ? top.Max(t => t.Score) : 0f;
            var pageSpread = top.Select(t => t.Page).Distinct().Count();

            if (bestScore < THRESHOLD && pageSpread >= 3)
            {
                THRESHOLD = Math.Max(0.05f, THRESHOLD - 0.05f);
            }

            // If retrieval is empty or below threshold → deterministic fallbacks
            if (top.Count == 0 || bestScore < THRESHOLD)
            {
                // Generic lexical fallback
                var fb = QaRetrieval.KeywordFallback(list, qNorm, k: desiredK);
                if (fb.Count == 0)
                {
                    var msg =
                        "I can't find that in the document. " +
                        "Based on the indexed text and search, there doesn't seem to be a clear, explicit answer to this question.";

                    await ctx.Response.WriteAsync($"event: token\ndata: {{\"text\":\"{msg}\"}}\n\n");
                    await ctx.Response.WriteAsync("event: citations\ndata: []\n\n");

                    var answerText = msg;
                    SaveMessage("assistant", answerText, Array.Empty<int>(), Array.Empty<int>());

                    await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                    await ctx.Response.Body.FlushAsync();
                    return;
                }

                var stitchedFb = ContextStitching.ExpandWithNeighbors(list, fb,
                    sideNeighbors: techGroup ? 2 : 1,
                    maxTotalNeighbors: techGroup ? 10 : 6);
                context = string.Join("\n\n", stitchedFb.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            }
            else
            {
                // Normal stitched context from 'top'
                var stitchedTop = ContextStitching.ExpandWithNeighbors(list, top,
                    sideNeighbors: techGroup ? 2 : 1,
                    maxTotalNeighbors: techGroup ? 10 : 6);
                context = string.Join("\n\n", stitchedTop.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            }
        }

        // ==== Stream the model output ====
        var chat2 = new OpenAI.Chat.ChatClient(model: answerModel, apiKey);
        var prompt2 = $"""
You are helping a student understand a specific PDF.
Use the Context below as your PRIMARY source of truth.
Prefer to quote or paraphrase what is actually in the Context.

If the question is general (for example: "What is this paper about?",
"Summarize the key findings", "What are the main limitations?"),
you should synthesize and summarize across all relevant parts of the Context.

If the question is very specific (for example: "What is the value of X?",
"On which page does Y happen?"), only answer if you can clearly
see that information in the Context. If you truly cannot find it,
then say: "I can't find that in the document."

Do not invent details that contradict the Context. If you need to be
vague because the Context is thin, say so explicitly.

When listing, include ALL items you find in Context; don't guess extra items.
Every bullet line MUST end with a [p:X] chip.
If the user asks to summarize in N bullets, you must return exactly N bullet
points numbered 1..N; do not produce fewer than N bullets even if content
seems limited—split broader points as needed.
{(string.IsNullOrWhiteSpace(summaryHint) ? "" : summaryHint + "\n")}
{(string.IsNullOrWhiteSpace(catHint) ? "" : catHint + "\n")}

Question: {qNorm}

Context:
{context}
""";


        var updates2 = chat2.CompleteChatStreaming(prompt2);
        var sb2 = new System.Text.StringBuilder();

        foreach (var update in updates2)
        {
            if (ctx.RequestAborted.IsCancellationRequested) break;
            if (update.ContentUpdate.Count > 0)
            {
                var piece = update.ContentUpdate[0].Text ?? "";
                sb2.Append(piece);
                await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = piece })}\n\n");
                await ctx.Response.Body.FlushAsync();
            }
        }
        var answer2 = sb2.ToString();
        var pages2 = Regex.Matches(answer2, @"\[\s*p\s*:\s*(\d+)\s*\]", RegexOptions.IgnoreCase)
                          .Select(m => int.Parse(m.Groups[1].Value))
                          .Distinct()
                          .ToArray();

        await ctx.Response.WriteAsync($"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(pages2)}\n\n");

        SaveMessage("assistant", answer2, pages2, pages2);

        await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
        await ctx.Response.Body.FlushAsync();

    }
    catch (Exception ex)
    {
        var err = System.Text.Json.JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        await ctx.Response.WriteAsync($"event: error\ndata: {err}\n\n");
        await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});





app.MapGet("/index/status/{uploadId:guid}", (Guid uploadId, IWebHostEnvironment env) =>
{
    var id = uploadId.ToString();
    var inMemory = InMemoryStore.VectorIndex.TryGetValue(id, out var list) && list?.Count > 0;

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
    var onDisk = System.IO.File.Exists(indexPath);

    int? chunks = null;
    if (onDisk && !inMemory)
    {
        try
        {
            var json = System.IO.File.ReadAllText(indexPath);
            var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
            chunks = rows?.Length;
        }
        catch { /* ignore */ }
    }
    else if (inMemory)
    {
        chunks = list!.Count;
    }

    return Results.Json(new { uploadId = id, inMemory, onDisk, chunks });
});


// Tutor endpoints disabled for MVP (kept only to avoid breaking clients/UI).
app.MapPost("/tutor/start/{uploadId:guid}", (Guid uploadId) =>
{
    return Results.Ok(new
    {
        status = "disabled",
        message = "Guided mode is disabled for this version. Use the Chat Q&A endpoints (/ask or /ask/stream) instead.",
        uploadId
    });
});

app.MapPost("/tutor/step", () =>
{
    return Results.Ok(new
    {
        status = "disabled",
        message = "Guided mode is disabled. Continue with the Chat Q&A endpoints for questions about the document."
    });
});


app.MapGet("/uploads/mine", async (HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT
            UploadId,
            Name,
            OriginalFileName,
            CreatedAt
        FROM Uploads
        WHERE UserId = $me
        ORDER BY datetime(CreatedAt) DESC";
    cmd.Parameters.AddWithValue("$me", me);

    using var r = await cmd.ExecuteReaderAsync();
    var list = new List<object>();
    while (await r.ReadAsync())
    {
        list.Add(new
        {
            uploadId = r.GetString(0),
            name = r.IsDBNull(1) ? "" : r.GetString(1),
            originalFileName = r.IsDBNull(2) ? "" : r.GetString(2),
            createdAt = r.GetString(3)
        });
    }
    return Results.Json(list);
});



// GET /sessions/mine -> list sessions for current user (with stats + lastMessagePreview)
app.MapGet("/sessions/mine", async (HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT
            s.Id AS SessionId,
            s.UploadId,
            COALESCE(u.Name, u.OriginalFileName, 'Untitled case') AS CaseName,

            -- Treat the earliest message time as the session 'createdAt'
            MIN(COALESCE(m.CreatedAt, datetime('now'))) AS CreatedAt,

            -- Last activity is the latest message time
            MAX(COALESCE(m.CreatedAt, datetime('now'))) AS LastActivityAt,

            -- Duration in seconds between first and last message
            CAST(
                (julianday(MAX(COALESCE(m.CreatedAt, datetime('now')))) -
                 julianday(MIN(COALESCE(m.CreatedAt, datetime('now'))))) * 86400.0
                AS INTEGER
            ) AS DurationSec,

            COUNT(m.Id) AS MessageCount,

            (
                SELECT COUNT(1)
                FROM Notes n
                WHERE n.SessionId = s.Id
            ) AS NotesCount,

            substr(
  COALESCE(
    (
      SELECT m2.Content
      FROM Messages m2
      WHERE m2.SessionId = s.Id
        AND (m2.Role = 'user' OR m2.Role IS NULL OR m2.Role = '')
      ORDER BY datetime(m2.CreatedAt) DESC, m2.Id DESC
      LIMIT 1
    ),
    (
      SELECT m3.Content
      FROM Messages m3
      WHERE m3.SessionId = s.Id
      ORDER BY datetime(m3.CreatedAt) DESC, m3.Id DESC
      LIMIT 1
    )
  ),
  1,
  80
) AS LastMessagePreview

        FROM Sessions s
        LEFT JOIN Uploads u ON u.UploadId = s.UploadId
        LEFT JOIN Messages m ON m.SessionId = s.Id
        WHERE s.UserId = $me
        GROUP BY
            s.Id,
            s.UploadId,
            COALESCE(u.Name, u.OriginalFileName, 'Untitled case')
        ORDER BY datetime(LastActivityAt) DESC;
    ";
    cmd.Parameters.AddWithValue("$me", me);

    var sessions = new List<object>();
    using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var sessionId = r.GetString(0);
        var uploadId = r.IsDBNull(1) ? null : r.GetString(1);
        var caseName = r.IsDBNull(2) ? "Untitled case" : r.GetString(2);
        var createdAt = r.IsDBNull(3) ? null : r.GetString(3);
        var lastActivityAt = r.IsDBNull(4) ? null : r.GetString(4);
        var durationSec = r.IsDBNull(5) ? 0 : r.GetInt32(5);
        var messageCount = r.IsDBNull(6) ? 0 : r.GetInt32(6);
        var notesCount = r.IsDBNull(7) ? 0 : r.GetInt32(7);
        var lastMessagePreview = r.IsDBNull(8) ? null : r.GetString(8);

        sessions.Add(new
        {
            sessionId,
            uploadId,
            caseName,
            createdAt,
            lastActivityAt,
            durationSec,
            messageCount,
            notesCount,
            lastMessagePreview
        });
    }

    return Results.Json(sessions);
});


// POST /sessions  -> create a chat thread (optionally tied to an upload)
app.MapPost("/sessions", async (HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    var root = doc.RootElement;
    var uploadId = root.TryGetProperty("uploadId", out var u) ? u.GetString() : null;

    var sessionId = Guid.NewGuid().ToString("N");
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"INSERT INTO Sessions (Id, UserId, UploadId, CreatedAt)
                        VALUES ($id, $user, $upload, $ts)";
    cmd.Parameters.AddWithValue("$id", sessionId);
    cmd.Parameters.AddWithValue("$user", me);
    cmd.Parameters.AddWithValue("$upload", (object?)uploadId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
    await cmd.ExecuteNonQueryAsync();

    return Results.Json(new { sessionId });
});

// GET /sessions/{id} -> full message history for a single session
app.MapGet("/sessions/{id}", async (string id, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // 1) Ensure this session belongs to the current user
    using (var chk = conn.CreateCommand())
    {
        chk.CommandText = @"
            SELECT UploadId
            FROM Sessions
            WHERE Id = $id AND UserId = $me
            LIMIT 1";
        chk.Parameters.AddWithValue("$id", id);
        chk.Parameters.AddWithValue("$me", me);

        var uploadIdObj = await chk.ExecuteScalarAsync();
        if (uploadIdObj is null)
        {
            // Either session doesn't exist or doesn't belong to this user
            return Results.NotFound(new { error = "not found" });
        }
    }

    // 2) Load all messages for this session, ordered by CreatedAt then Id
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT
            Role,
            Content,
            Citations,
            PagesUsed,
            CreatedAt
        FROM Messages
        WHERE SessionId = $id
        ORDER BY datetime(CreatedAt) ASC, Id ASC";
    cmd.Parameters.AddWithValue("$id", id);

    var messages = new List<object>();
    using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var role = r.GetString(0);
        var content = r.GetString(1);

        int[]? citations = null;
        int[]? pagesUsed = null;

        if (!r.IsDBNull(2))
        {
            var citesJson = r.GetString(2);
            try
            {
                citations = System.Text.Json.JsonSerializer.Deserialize<int[]>(citesJson);
            }
            catch { citations = null; }
        }

        if (!r.IsDBNull(3))
        {
            var pagesJson = r.GetString(3);
            try
            {
                pagesUsed = System.Text.Json.JsonSerializer.Deserialize<int[]>(pagesJson);
            }
            catch { pagesUsed = null; }
        }

        var createdAt = r.GetString(4);

        messages.Add(new
        {
            role,
            content,
            citations = citations ?? Array.Empty<int>(),
            pagesUsed = pagesUsed ?? Array.Empty<int>(),
            createdAt
        });
    }

    return Results.Json(messages);
});


// GET /sessions/{id}/notes -> list notes for a session (current user only)
app.MapGet("/sessions/{id}/notes", async (string id, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // 1) Check that this session belongs to the current user
    using (var chk = conn.CreateCommand())
    {
        chk.CommandText = @"
            SELECT UploadId
            FROM Sessions
            WHERE Id = $id AND UserId = $me
            LIMIT 1";
        chk.Parameters.AddWithValue("$id", id);
        chk.Parameters.AddWithValue("$me", me);

        var sess = await chk.ExecuteScalarAsync();
        if (sess is null)
        {
            return Results.NotFound(new { error = "not found" });
        }
    }

    // 2) Load notes for this session & user
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT Id, Text, CreatedAt
        FROM Notes
        WHERE SessionId = $id AND UserId = $me
        ORDER BY datetime(CreatedAt) ASC, Id ASC";
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$me", me);

    var notes = new List<object>();
    using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var noteId = r.GetInt64(0);
        var text = r.GetString(1);
        var createdAt = r.GetString(2);

        notes.Add(new
        {
            id = noteId,
            text,
            createdAt
        });
    }

    return Results.Json(notes);
});

// POST /sessions/{id}/notes -> add a note to a session
app.MapPost("/sessions/{id}/notes", async (string id, SessionNoteCreateDto input, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    if (string.IsNullOrWhiteSpace(input.Text))
    {
        return Results.BadRequest(new { error = "text_required" });
    }

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // 1) Check that this session belongs to the current user and get UploadId
    string? uploadId = null;
    using (var chk = conn.CreateCommand())
    {
        chk.CommandText = @"
            SELECT UploadId
            FROM Sessions
            WHERE Id = $id AND UserId = $me
            LIMIT 1";
        chk.Parameters.AddWithValue("$id", id);
        chk.Parameters.AddWithValue("$me", me);

        var uploadIdObj = await chk.ExecuteScalarAsync();
        if (uploadIdObj is null)
        {
            return Results.NotFound(new { error = "not found" });
        }

        if (uploadIdObj is string s && !string.IsNullOrWhiteSpace(s))
        {
            uploadId = s;
        }
    }

    // 2) Insert the note
    var createdAt = DateTime.UtcNow.ToString("o");

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            INSERT INTO Notes (UserId, SessionId, UploadId, Text, CreatedAt)
            VALUES ($userId, $sessionId, $uploadId, $text, $createdAt)";
        cmd.Parameters.AddWithValue("$userId", me);
        cmd.Parameters.AddWithValue("$sessionId", id);
        cmd.Parameters.AddWithValue("$uploadId", (object?)uploadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$text", input.Text);
        cmd.Parameters.AddWithValue("$createdAt", createdAt);

        await cmd.ExecuteNonQueryAsync();
    }

    long noteId;
    using (var idCmd = conn.CreateCommand())
    {
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var scalar = await idCmd.ExecuteScalarAsync();
        noteId = scalar is long l ? l : Convert.ToInt64(scalar);
    }

    return Results.Json(new
    {
        id = noteId,
        text = input.Text,
        createdAt
    });
});

// PATCH /uploads/{uploadId}/name -> rename a case for the current user
app.MapPatch("/uploads/{uploadId:guid}/name", async (Guid uploadId, RenameUploadDto input, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";

    if (string.IsNullOrWhiteSpace(input.Name))
    {
        return Results.BadRequest(new { error = "name_required" });
    }

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        UPDATE Uploads
        SET OriginalFileName = $name
        WHERE UploadId = $u AND UserId = $me";
    cmd.Parameters.AddWithValue("$name", input.Name.Trim());
    cmd.Parameters.AddWithValue("$u", uploadId);
    cmd.Parameters.AddWithValue("$me", me);

    var rows = await cmd.ExecuteNonQueryAsync();
    if (rows == 0)
    {
        // Either upload doesn't exist or not owned by this user
        return Results.NotFound(new { error = "not_found" });
    }

    return Results.Json(new
    {
        uploadId,
        name = input.Name.Trim()
    });
});



// DELETE /uploads/{uploadId} -> delete a case and its sessions/messages/notes/files for current user
app.MapDelete("/uploads/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    var id = uploadId.ToString(); // string version used for files / notes

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // 1) Check ownership
    using (var chk = conn.CreateCommand())
    {
        chk.CommandText = @"
            SELECT 1
            FROM Uploads
            WHERE UploadId = $u AND UserId = $me
            LIMIT 1";
        chk.Parameters.AddWithValue("$u", uploadId);   // <-- use Guid here
        chk.Parameters.AddWithValue("$me", me);

        var ok = await chk.ExecuteScalarAsync();
        if (ok is null)
        {
            return Results.NotFound(new { error = "not_found" });
        }
    }

    // 2) Gather session ids for this upload
    var sessionIds = new List<string>();
    using (var scmd = conn.CreateCommand())
    {
        scmd.CommandText = @"
            SELECT Id
            FROM Sessions
            WHERE UploadId = $u AND UserId = $me";
        scmd.Parameters.AddWithValue("$u", uploadId);  // <-- Guid
        scmd.Parameters.AddWithValue("$me", me);

        using var r = await scmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            sessionIds.Add(r.GetString(0));
        }
    }

    // 3) Delete messages for those sessions
    foreach (var sid in sessionIds)
    {
        using var mcmd = conn.CreateCommand();
        mcmd.CommandText = "DELETE FROM Messages WHERE SessionId = $sid";
        mcmd.Parameters.AddWithValue("$sid", sid);
        await mcmd.ExecuteNonQueryAsync();
    }

    // 4) Delete notes tied to those sessions
    foreach (var sid in sessionIds)
    {
        using var ncmd = conn.CreateCommand();
        ncmd.CommandText = "DELETE FROM Notes WHERE SessionId = $sid";
        ncmd.Parameters.AddWithValue("$sid", sid);
        await ncmd.ExecuteNonQueryAsync();
    }

    // 5) Delete notes tied directly to this upload (Notes.UploadId is string)
    using (var n2 = conn.CreateCommand())
    {
        n2.CommandText = "DELETE FROM Notes WHERE UploadId = $u";
        n2.Parameters.AddWithValue("$u", id);          // <-- string id
        await n2.ExecuteNonQueryAsync();
    }

    // 6) Delete sessions for this upload
    using (var scmd2 = conn.CreateCommand())
    {
        scmd2.CommandText = "DELETE FROM Sessions WHERE UploadId = $u AND UserId = $me";
        scmd2.Parameters.AddWithValue("$u", uploadId); // <-- Guid
        scmd2.Parameters.AddWithValue("$me", me);
        await scmd2.ExecuteNonQueryAsync();
    }

    // 7) Delete the upload row
    using (var ucmd = conn.CreateCommand())
    {
        ucmd.CommandText = "DELETE FROM Uploads WHERE UploadId = $u AND UserId = $me";
        ucmd.Parameters.AddWithValue("$u", uploadId);  // <-- Guid
        ucmd.Parameters.AddWithValue("$me", me);
        await ucmd.ExecuteNonQueryAsync();
    }

    // 8) Clear in-memory index
    InMemoryStore.VectorIndex.Remove(id);

    // 9) Delete files on disk
    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var pdfPath = Path.Combine(uploadsRoot, $"{id}.pdf");
    var summaryPath = Path.Combine(uploadsRoot, $"{id}.summary.json");
    var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");

    try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
    try { if (File.Exists(summaryPath)) File.Delete(summaryPath); } catch { }
    try { if (File.Exists(indexPath)) File.Delete(indexPath); } catch { }

    return Results.Json(new { uploadId = id, deleted = true });
});


// DELETE /sessions/{sessionId} -> delete a session + its messages + notes (current user only)
app.MapDelete("/sessions/{sessionId}", async (string sessionId, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    if (string.IsNullOrEmpty(me))
    {
        return Results.Unauthorized();
    }

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // Make sure this session belongs to the current user
    using (var checkCmd = conn.CreateCommand())
    {
        checkCmd.CommandText = "SELECT COUNT(1) FROM Sessions WHERE Id = $id AND UserId = $me";
        checkCmd.Parameters.AddWithValue("$id", sessionId);
        checkCmd.Parameters.AddWithValue("$me", me);

        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        if (!exists)
        {
            return Results.NotFound(new { error = "session not found" });
        }
    }

    // Delete child rows first (if you don't have FOREIGN KEY CASCADE)
    using (var delMsg = conn.CreateCommand())
    {
        delMsg.CommandText = "DELETE FROM Messages WHERE SessionId = $id";
        delMsg.Parameters.AddWithValue("$id", sessionId);
        await delMsg.ExecuteNonQueryAsync();
    }

    using (var delNotes = conn.CreateCommand())
    {
        delNotes.CommandText = "DELETE FROM Notes WHERE SessionId = $id";
        delNotes.Parameters.AddWithValue("$id", sessionId);
        await delNotes.ExecuteNonQueryAsync();
    }

    using (var delSession = conn.CreateCommand())
    {
        delSession.CommandText = "DELETE FROM Sessions WHERE Id = $id AND UserId = $me";
        delSession.Parameters.AddWithValue("$id", sessionId);
        delSession.Parameters.AddWithValue("$me", me);
        await delSession.ExecuteNonQueryAsync();
    }

    return Results.NoContent();
});


// --- Admin: list all sessions for supervision (superuser only) ---
app.MapGet("/admin/sessions", async (HttpContext ctx) =>
{
    // Must be authenticated
    var me = ctx.Items["userId"] as string;
    var isSuper = ctx.Items["isSuperUser"] as bool? ?? false;

    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    if (!isSuper)
    {
        return Results.Forbid();
    }

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
SELECT
    s.Id                AS SessionId,
    s.UserId            AS UserId,
    IFNULL(u.Email, '') AS UserEmail,
    IFNULL(u.FullName,'') AS UserFullName,
    s.UploadId          AS UploadId,
    IFNULL(up.Name, '') AS CaseName,
    IFNULL(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt         AS SessionCreatedAt,
    (
        SELECT MAX(m.CreatedAt)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS LastMessageAt,
    (
        SELECT COUNT(1)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS MessageCount
FROM Sessions s
LEFT JOIN Users   u  ON u.Id       = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
ORDER BY s.CreatedAt DESC;
";

    var list = new List<object>();
    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                sessionId = reader.GetString(0),
                userId = reader.GetString(1),
                userEmail = reader.GetString(2),
                userFullName = reader.GetString(3),
                uploadId = reader.IsDBNull(4) ? null : reader.GetString(4),
                caseName = reader.IsDBNull(5) ? null : reader.GetString(5),
                originalFileName = reader.IsDBNull(6) ? null : reader.GetString(6),
                createdAt = reader.GetString(7),
                lastMessageAt = reader.IsDBNull(8) ? null : reader.GetString(8),
                messageCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
            });
        }
    }

    return Results.Ok(list);
});


// --- Admin: get details + messages for a specific session (superuser only) ---
app.MapGet("/admin/sessions/{sessionId}", async (string sessionId, HttpContext ctx) =>
{
    // Must be authenticated
    var me = ctx.Items["userId"] as string;
    var isSuper = ctx.Items["isSuperUser"] as bool? ?? false;

    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    if (!isSuper)
    {
        return Results.Forbid();
    }

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=ingestion.db;Cache=Shared");
    await conn.OpenAsync();

    // 1) Load session metadata + owner + upload info
    string? userId = null, userEmail = null, userFullName = null;
    string? uploadId = null, caseName = null, originalFileName = null, createdAt = null;

    var metaCmd = conn.CreateCommand();
    metaCmd.CommandText = @"
SELECT
    s.Id                AS SessionId,
    s.UserId            AS UserId,
    IFNULL(u.Email, '') AS UserEmail,
    IFNULL(u.FullName,'') AS UserFullName,
    s.UploadId          AS UploadId,
    IFNULL(up.Name, '') AS CaseName,
    IFNULL(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt         AS SessionCreatedAt
FROM Sessions s
LEFT JOIN Users   u  ON u.Id        = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
WHERE s.Id = $id
LIMIT 1;
";
    metaCmd.Parameters.AddWithValue("$id", sessionId);

    using (var r = await metaCmd.ExecuteReaderAsync())
    {
        if (!await r.ReadAsync())
        {
            return Results.NotFound(new { error = "session not found" });
        }

        // s.Id is column 0 but we already have sessionId from the route
        userId = r.GetString(1);
        userEmail = r.GetString(2);
        userFullName = r.GetString(3);
        uploadId = r.IsDBNull(4) ? null : r.GetString(4);
        caseName = r.IsDBNull(5) ? null : r.GetString(5);
        originalFileName = r.IsDBNull(6) ? null : r.GetString(6);
        createdAt = r.GetString(7);
    }

    // 2) Load messages in this session
    var messages = new List<object>();
    var msgCmd = conn.CreateCommand();
    msgCmd.CommandText = @"
SELECT
    Id,
    Role,
    Content,
    Citations,
    PagesUsed,
    CreatedAt
FROM Messages
WHERE SessionId = $id
ORDER BY Id ASC;
";
    msgCmd.Parameters.AddWithValue("$id", sessionId);

    using (var r = await msgCmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            messages.Add(new
            {
                id = r.GetInt64(0),
                role = r.GetString(1),
                content = r.GetString(2),
                citations = r.IsDBNull(3) ? null : r.GetString(3),
                pagesUsed = r.IsDBNull(4) ? null : r.GetString(4),
                createdAt = r.GetString(5),
            });
        }
    }

    // 3) Return combined view
    return Results.Ok(new
    {
        sessionId,
        userId,
        userEmail,
        userFullName,
        uploadId,
        caseName,
        originalFileName,
        createdAt,
        messages
    });
});




app.Run();


// Classify a user's question into a high-level QuestionType using gpt-5-mini
static async Task<QuestionType> ClassifyQuestionAsync(string question)
{
    // Very short or empty → treat as Other
    if (string.IsNullOrWhiteSpace(question))
        return QuestionType.Other;

    // Later we can add some fast rule shortcuts here for obvious things (title/authors)

    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    // Small / cheap model just for classification
    var classifierModel = Environment.GetEnvironmentVariable("OPENAI_CLASSIFIER_MODEL")
        ?? "gpt-5-mini";

    // Notice: same pattern as your other ChatClient usages
    var client = new OpenAI.Chat.ChatClient(classifierModel, apiKey);

    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        new OpenAI.Chat.SystemChatMessage(
            "You classify user questions about a single document. " +
            "Return exactly ONE of these labels, and nothing else: " +
            "SUMMARY, FACT, METHOD, FINDINGS, WHY_EXPLAIN, OTHER."
        ),
        new OpenAI.Chat.UserChatMessage($"Question: {question}")
    };

    // Use the same style as AnswerWithContext: CompleteChat(messages).Value;
    var result = client.CompleteChat(messages).Value;
    var raw = string.Concat(result.Content.Select(part => part.Text ?? string.Empty));
    var label = raw.Trim().ToUpperInvariant();

    return label switch
    {
        "SUMMARY" => QuestionType.Summary,
        "FACT" => QuestionType.Fact,
        "METHOD" => QuestionType.Method,
        "FINDINGS" => QuestionType.Findings,
        "WHY_EXPLAIN" => QuestionType.WhyExplain,
        _ => QuestionType.Other
    };
}


static int WordToInt(string w) => (w ?? "").ToLowerInvariant() switch
{
    "one" => 1,
    "two" => 2,
    "three" => 3,
    "four" => 4,
    "five" => 5,
    "six" => 6,
    "seven" => 7,
    "eight" => 8,
    "nine" => 9,
    "ten" => 10,
    _ => 5
};





// Safe, bounds-checked head-of-string helper
static string SafeHead(string s, int max) =>
    string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

public record IndexedChunk(int Page, ReadOnlyMemory<float> Vec, string Preview);

public enum QuestionType
{
    Summary,    // "What is this paper about?", "Give an overview"
    Fact,       // "Who are the authors?", "When was this published?"
    Method,     // "What method did they use?", "How did they collect data?"
    Findings,   // "What did they find?", "What are the main results?"
    WhyExplain, // "Why did they choose this?", "Explain this in simpler terms"
    Other       // Anything else
}


public static class InMemoryStore
{
    public static readonly Dictionary<string, List<IndexedChunk>> VectorIndex = new();
}


class SessionNoteCreateDto
{
    public string Text { get; set; } = "";
}

class RenameUploadDto
{
    public string Name { get; set; } = "";
}



public class TutorChoice
{
    public string id { get; set; }
    public string label { get; set; }
}

public class TutorStepResponse
{
    public string narrative { get; set; }
    public TutorChoice[] choices { get; set; }
    public int[] cites { get; set; }
    public string stepSummary { get; set; }
}
public record TutorSession(
    string SessionId,
    Guid UploadId,
    string Category,
    string? Focus,
    int StepIndex,
    List<string> History
)
{
    public HashSet<int> VisitedCites { get; set; } = new();
    public int NoNewCitesStreak { get; set; } = 0;
    public List<int> LastCites { get; set; } = new();

}

public record TutorStepRequest(string SessionId, string ChoiceId);

public static class TutorStore
{
    public static readonly ConcurrentDictionary<string, TutorSession> Sessions = new();
}

public record CaseDto(string Id, string Name, int Pages, int Images, double SizeMB, string UploadedAt);
public record SerializableChunk(int Page, string Preview, float[] Vec);





// ---------------- helpers ----------------
static class PdfImageUtils
{
    private sealed class ImageCounterListener : IEventListener
    {
        public int Count { get; private set; }
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_IMAGE)
                Count++;
        }
        public ICollection<EventType> GetSupportedEvents() => null;
    }

    public static int CountRasterImagesExact(string path)
    {
        using var pdf = new iText.Kernel.Pdf.PdfDocument(new PdfReader(path));
        int total = 0;
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
        {
            var listener = new ImageCounterListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(pdf.GetPage(i));
            total += listener.Count;
        }
        return total;
    }
}


public static class TextChunking
{
    public static IEnumerable<string> ChunkBySize(string text, int maxChars = 1000, int overlap = 160)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        if (maxChars <= 0) yield break;
        if (overlap < 0) overlap = 0;
        int i = 0;
        while (i < text.Length)
        {
            int end = Math.Min(text.Length, i + maxChars);
            int softEnd = end;
            for (int j = end - 1; j > i + maxChars / 2; j--)
            {
                char c = text[j];
                if (c == '.' || c == '!' || c == '?' || char.IsWhiteSpace(c)) { softEnd = j + 1; break; }
            }
            end = softEnd;
            var slice = text.AsSpan(i, end - i).ToString().Trim();
            if (!string.IsNullOrWhiteSpace(slice)) yield return slice;
            if (end >= text.Length) yield break;
            i = Math.Max(end - overlap, i + 1);
        }
    }
}



public static class IndexPersistence
{
    public static bool TryLoad(Guid uploadId, IWebHostEnvironment env, out List<IndexedChunk> list)
    {
        var id = uploadId.ToString();
        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
        var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
        list = null!;

        if (!File.Exists(indexPath)) return false;

        var json = File.ReadAllText(indexPath);
        var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
        if (rows is null || rows.Length == 0) return false;

        list = rows.Select(r => new IndexedChunk(
            Page: r.Page,
            Vec: new ReadOnlyMemory<float>(r.Vec),
            Preview: r.Preview
        )).ToList();

        InMemoryStore.VectorIndex[id] = list;
        return true;
    }
}

// Return shape used by both routes
public record TopChunk(int Page, string Preview, float Score);

public static class QaRetrieval
{
    // --- Query understanding ---
    public static bool IsListy(string q)
    {
        var s = q ?? string.Empty;

        // Common list verbs & phrasings
        if (Regex.IsMatch(s, @"\b(list|all|which|enumerate|show|show me|give|give me|name|return|extract|identify|find all|find every|every|provide|report|catalog|compile|what\s+are|what\s+were)\b", RegexOptions.IgnoreCase))
            return true;

        // Numeric / date cues (often imply multiple items)
        if (Regex.IsMatch(s, @"[%+]", RegexOptions.IgnoreCase)) return true;                                     // %, +
        if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b", RegexOptions.IgnoreCase)) return true;                    // years
        if (Regex.IsMatch(s, @"\b(date|dates|range|ranges|deadline|deadlines)\b", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(s, @"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b", RegexOptions.IgnoreCase))
            return true;

        // Category words that commonly yield lists
        if (Regex.IsMatch(s, @"\b(languages?|frameworks?|libraries|databases?|tools?|certifications?|people|persons|authors?|organizations?|countries|requirements?|risks?|achievements?|metrics?|publications?|references?)\b", RegexOptions.IgnoreCase))
            return true;

        // Tech umbrella terms imply lists (wider recall)
        if (Regex.IsMatch(s, @"\b(technologies?|tech\s*stack|technology\s*stack|stack|skills|technical\s+skills)\b", RegexOptions.IgnoreCase))
            return true;



        return false;
    }


    // Tokenize to alnum lowercase
    private static string[] Tokens(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        return Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]{2,}")
                    .Select(m => m.Value)
                    .ToArray();
    }

    // Lightweight lexical score (no external engine)
    private static float LexicalScore(string preview, HashSet<string> qset)
    {
        if (string.IsNullOrEmpty(preview) || qset.Count == 0) return 0f;
        var p = preview.ToLowerInvariant();

        float s = 0f;
        foreach (var t in qset) if (p.Contains(t)) s += 1f;

        if (p.Contains("@")) s += 0.5f;          // emails
        if (Regex.IsMatch(p, @"\b\d{4}\b")) s += 0.25f; // years/dates
        return s;
    }

    // Tiny presence boost (kept small)
    private static float Boost(string preview, HashSet<string> qset)
    {
        var p = preview?.ToLowerInvariant() ?? "";
        float b = 0f;
        foreach (var t in qset)
        {
            if (p.Contains(t)) { b += 0.03f; if (b >= 0.09f) break; }
        }
        if (p.Contains("@")) b += 0.02f;
        return Math.Min(b, 0.10f);
    }

    // Fallback: keyword scan to grab a broader block (generic, not resume-specific)
    public static List<TopChunk> KeywordFallback(List<IndexedChunk> list, string q, int k = 8)
    {
        var qset = new HashSet<string>(Tokens(q));
        if (qset.Count == 0) return new List<TopChunk>();

        return list
            .Select(x => new { x.Page, x.Preview, lex = LexicalScore(x.Preview, qset) })
            .Where(r => r.lex > 0)
            .OrderByDescending(r => r.lex)
            .Take(k)
            .Select(r => new TopChunk(r.Page, r.Preview, 0.16f))
            .ToList();
    }


    // Main selection with hybrid score + MMR + list-mode + optional page dedupe
    public static List<TopChunk> SelectTop(
     List<IndexedChunk> list,
     ReadOnlySpan<float> qVec,
     string q,
     bool forStreaming)
    {
        bool listy = IsListy(q);

        // More coverage for lists; conservative for non-lists
        int K = listy ? 12 : (forStreaming ? 3 : 3);

        var qset = new HashSet<string>(Tokens(q));
        const float alpha = 0.85f; // embedding weight
        const float beta = 0.15f; // lexical weight

        // Avoid ref-like span use in lambdas
        float[] qVecArr = qVec.ToArray();

        // 1) score candidates (oversample before MMR)
        var cands = list.Select(x =>
        {
            var cos = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(qVecArr, x.Vec.Span);
            var lex = LexicalScore(x.Preview, qset);
            var boo = Boost(x.Preview, qset);
            var fin = alpha * cos + beta * lex + boo;
            return new Cand(x.Page, x.Preview, x.Vec, cos, lex, boo, fin);
        })
        .OrderByDescending(c => c.Final)
        .Take(Math.Max(K * 4, 12))
        .ToList();

        // 2) MMR for diversity
        var picked = MMR(cands, K, lambda: 0.7f);

        // 3) Dedupe-by-page ONLY for non-list queries.
        if (!listy)
        {
            picked = picked
                .GroupBy(c => c.Page)
                .Select(g => g.First())
                .ToList();
        }

        // 4) return lightweight tops
        return picked.Select(c => new TopChunk(c.Page, c.Preview, c.Final)).ToList();
    }


    // ----- internals -----
    private record Cand(int Page, string Preview, ReadOnlyMemory<float> Vec, float Cos, float Lex, float Boost, float Final);

    private static List<Cand> MMR(List<Cand> cands, int K, float lambda)
    {
        var chosen = new List<Cand>();
        var remaining = new List<Cand>(cands);

        while (chosen.Count < K && remaining.Count > 0)
        {
            Cand best = null;
            float bestScore = float.NegativeInfinity;

            foreach (var c in remaining)
            {
                float div = 0f;
                foreach (var s in chosen)
                {
                    var sim = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(c.Vec.Span, s.Vec.Span);
                    if (sim > div) div = sim;
                }
                float score = lambda * c.Final - (1 - lambda) * div;
                if (score > bestScore) { bestScore = score; best = c; }
            }

            chosen.Add(best);
            remaining.Remove(best);
        }

        return chosen;
    }
}


// ---- Context stitching: include immediate neighbor chunks on the same page ----
public static class ContextStitching
{
    // Adds up to `sideNeighbors` neighbors on each side per picked chunk (same page),
    // up to `maxTotalNeighbors` total across all picks. Keeps order by doc position.
    public static List<TopChunk> ExpandWithNeighbors(
        List<IndexedChunk> all,
        List<TopChunk> picks,
        int sideNeighbors = 1,
        int maxTotalNeighbors = 6)
    {
        if (picks == null || picks.Count == 0) return picks ?? new List<TopChunk>();

        // Build a (page, preview) -> index map to recover original order
        var order = new Dictionary<(int page, string preview), int>();
        for (int i = 0; i < all.Count; i++)
        {
            var key = (all[i].Page, all[i].Preview);
            if (!order.ContainsKey(key)) order[key] = i;
        }

        var result = new List<TopChunk>(picks);
        var seen = new HashSet<string>(picks.Select(p => $"{p.Page}\u0001{p.Preview}"));
        int added = 0;

        foreach (var p in picks)
        {
            var key = (p.Page, p.Preview);
            if (!order.TryGetValue(key, out var idx)) continue;

            for (int offset = 1; offset <= sideNeighbors; offset++)
            {
                // previous neighbor on same page
                if (added < maxTotalNeighbors && idx - offset >= 0 && all[idx - offset].Page == p.Page)
                {
                    var prev = all[idx - offset];
                    var k = $"{prev.Page}\u0001{prev.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(prev.Page, prev.Preview, p.Score * 0.99f));
                        added++;
                    }
                }
                // next neighbor on same page
                if (added < maxTotalNeighbors && idx + offset < all.Count && all[idx + offset].Page == p.Page)
                {
                    var next = all[idx + offset];
                    var k = $"{next.Page}\u0001{next.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(next.Page, next.Preview, p.Score * 0.99f));
                        added++;
                    }
                }
            }

            if (added >= maxTotalNeighbors) break;
        }

        // Preserve document order
        result = result
            .OrderBy(t => order.TryGetValue((t.Page, t.Preview), out var i) ? i : int.MaxValue)
            .ToList();

        return result;
    }
}


// ----- Category detector: adds a small, query-aware precision hint -----
public record CategoryHint(string Name, string PromptHint);

public static class CategoryDetector
{
    public static CategoryHint Detect(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return new CategoryHint("none", "");

        var s = q.ToLowerInvariant();

        // Technologies / tech stack / skills → group by category
        if (Regex.IsMatch(s, @"\b(tech(?:nologies?)?|tech\s*stack|technology\s*stack|stack|skills|technical\s+skills)\b"))
            return new CategoryHint(
                "tech_group",
                "Group the answer into labeled sections: " +
                "1) Programming languages, 2) Frameworks & libraries, 3) Databases & data stores, " +
                "4) Tools & platforms (including DevOps/hosting), 5) Methodologies & practices. " +
                "Under each section, include only items that strictly belong to that category and exclude close neighbors. " +
                "If a section has no items, omit it."
            );


        // Programming languages
        if (Regex.IsMatch(s, @"\b(programming\s+languages?|coding\s+languages?)\b"))
            return new CategoryHint("programming_languages",
                "For programming languages, exclude frameworks, libraries, tools, databases, and model names.");

        // Frameworks / libraries
        if (Regex.IsMatch(s, @"\b(frameworks?|libraries|packages|toolkits?)\b"))
            return new CategoryHint("frameworks_libraries",
                "For frameworks and libraries, exclude programming languages, databases, and general tools.");

        // Databases / data stores
        if (Regex.IsMatch(s, @"\b(databases?|data\s*stores?|dbs?)\b"))
            return new CategoryHint("databases",
                "For databases and data stores, exclude programming languages, frameworks/libraries, and tools.");

        // Schools
        if (Regex.IsMatch(s, @"\b(universit(?:y|ies)|college(?:s)?|school(?:s)?)\b"))
            return new CategoryHint("schools",
                "For universities/colleges/schools, exclude degrees, departments, programs, and locations.");

        // People
        if (Regex.IsMatch(s, @"\b(people|persons|person\s+names?|authors?|speakers?|presenters?)\b"))
            return new CategoryHint("people",
                "For people, include person names only; exclude organizations, teams, and roles without names.");

        // Organizations
        if (Regex.IsMatch(s, @"\b(organizations?|companies|institutions|agencies)\b"))
            return new CategoryHint("organizations",
                "For organizations, exclude person names and job titles.");

        // Countries
        if (Regex.IsMatch(s, @"\b(countries?)\b"))
            return new CategoryHint("countries",
                "For countries, exclude cities, states/provinces, and regions.");

        // Dates / date ranges (months, years, deadlines)
        if (Regex.IsMatch(s, @"\b(dates?|date\s*ranges?|deadlines?)\b") ||
            Regex.IsMatch(s, @"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b") ||
            Regex.IsMatch(s, @"\b(19|20)\d{2}\b"))
            return new CategoryHint("dates",
                "For dates, return only explicit date expressions as written (e.g., 11/2021–07/2023); exclude durations without dates.");

        // Quantified metrics / achievements
        if (Regex.IsMatch(s, @"%|percent|percentage|\bplus\b|\b\+\b|\bmetrics?\b|\bachievements?\b"))
            return new CategoryHint("metrics",
                "For quantified achievements or metrics, return only items that include a percentage (%) or a plus-count (+), exactly as written.");

        return new CategoryHint("none", "");
    }
}

// ---------- Query normalization ----------
public static class QueryNormalization
{
    public static string Normalize(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return q ?? "";
        var s = q.Trim();

        // Strip polite prefixes
        s = Regex.Replace(s, @"^\s*(can you|could you|please|kindly|would you|i want to|i would like to|could u|can u)\s+", "", RegexOptions.IgnoreCase);

        // Map synonyms to tighter phrasing
        s = Regex.Replace(s, @"\b(name of (the )?document)\b", "document title", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(pdf title|title of the pdf)\b", "document title", RegexOptions.IgnoreCase);
        // QueryNormalization.Normalize(...)
        s = Regex.Replace(s, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                          "document title", RegexOptions.IgnoreCase);


        // Collapse whitespace
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }
}

// ---------- Section intent (generic for reports/papers) ----------
public enum SectionIntent { None, Abstract, Title, Authors, Affiliations, Introduction, Conclusion, References, Keywords }

public static class SectionSwitchboard
{
    public static SectionIntent Detect(string q)
    {
        var s = q?.ToLowerInvariant() ?? "";
        if (Regex.IsMatch(s, @"\b(document title|title)\b")) return SectionIntent.Title;
        if (Regex.IsMatch(s, @"\babstract\b")) return SectionIntent.Abstract;
        if (Regex.IsMatch(s, @"\bauthors?\b")) return SectionIntent.Authors;
        if (Regex.IsMatch(s, @"\baffiliations?\b")) return SectionIntent.Affiliations;
        if (Regex.IsMatch(s, @"\b(introduction|background)\b")) return SectionIntent.Introduction;
        if (Regex.IsMatch(s, @"\b(conclusion|conclusions)\b")) return SectionIntent.Conclusion;
        if (Regex.IsMatch(s, @"\b(references|bibliography|works\s+cited)\b")) return SectionIntent.References;
        if (Regex.IsMatch(s, @"\bkeywords?\b")) return SectionIntent.Keywords;
        return SectionIntent.None;
    }

    public static List<TopChunk> FindSection(List<IndexedChunk> list, SectionIntent intent)
    {
        string pattern = intent switch
        {
            SectionIntent.Abstract => @"\babstract\b",
            SectionIntent.Introduction => @"\bintroduction\b",
            SectionIntent.Conclusion => @"\bconclusions?\b",
            SectionIntent.References => @"\b(references|bibliography|works\s+cited)\b",
            SectionIntent.Keywords => @"\bkeywords?\b",
            // Authors/Affiliations are weak as headings; still try
            SectionIntent.Authors => @"\bauthors?\b",
            SectionIntent.Affiliations => @"\baffiliations?\b",
            _ => ""
        };
        if (string.IsNullOrEmpty(pattern)) return new List<TopChunk>();

        var hits = list.Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
                       .GroupBy(x => x.Page)
                       .Select(g => g.First())
                       .OrderBy(x => x.Page)
                       .Select(x => new TopChunk(x.Page, x.Preview, 0.5f))
                       .ToList();
        return hits;
    }
}

// ---------- Text normalization (applied at index time) ----------
public static class TextNormalization
{
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;

        // Common PDF ligatures
        s = s.Replace("ﬁ", "fi").Replace("ﬂ", "fl");

        // Join hyphenated line breaks: foo-\nbar -> foobar
        s = Regex.Replace(s, @"(\w)-\s*\r?\n\s*(\w)", "$1$2");

        // Normalize whitespace
        s = s.Replace('\u00A0', ' ');
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        s = Regex.Replace(s, @"\s+\r?\n", "\n");

        return s;
    }
}

// ---------- PDF metadata helper (title/author fallback) ----------
public static class PdfMetadataHelper
{
    public static (string? Title, string? Author) Read(Guid uploadId, IWebHostEnvironment env)
    {
        try
        {
            var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
            if (!File.Exists(path)) return (null, null);
            using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
            var info = pdf.GetDocumentInfo();
            var title = info?.GetTitle();
            var author = info?.GetAuthor();
            return (string.IsNullOrWhiteSpace(title) ? null : title,
                    string.IsNullOrWhiteSpace(author) ? null : author);
        }
        catch { return (null, null); }
    }
}


public static class TitleHeuristics
{
    public static string? FromPdfFirstPage(Guid uploadId, IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
        if (!File.Exists(path)) return null;

        using var doc = PdfPigDoc.Open(path);
        var first = doc.GetPages().FirstOrDefault();
        if (first == null) return null;
        // Build cleaned candidate lines
        var lines = (first.Text ?? "")
            .Split('\n')
            .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
            .Where(s => s.Length >= 8)
            .ToList();

        // Skip obvious non-title headers
        var blacklist = new Regex(@"\b(UNIVERSITY|DEPARTMENT|SCHOOL|FACULTY|COLLEGE|INSTITUTE|SUBMITTED|SUBMISSION|SUPERVISOR|ADVIS(ER|OR)|\bBY\b|APPROVAL|DECLARATION|ACKNOWLEDG(E)?MENTS?|SIGNATURE|NAME OF|INDEX|CERTIFICATE)\b",
                                  RegexOptions.IgnoreCase);

        // Scoring function: uppercase-ish + reasonable length; penalize blacklisted & date-y lines
        float Score(string s)
        {
            int letters = s.Count(char.IsLetter);
            int upper = s.Count(char.IsUpper);
            float upperRatio = letters == 0 ? 0f : (float)upper / letters;

            float score = 0;
            score += upperRatio >= 0.60f ? 3 : (upperRatio >= 0.40f ? 1 : 0);
            int len = s.Length;
            score += (len >= 30 && len <= 160) ? 3 : (len >= 20 && len <= 180 ? 1 : -2);
            if (blacklist.IsMatch(s)) score -= 6;
            if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b")) score -= 1; // years
            return score;
        }

        // Rank candidates
        var ranked = lines
            .Select((s, i) => new { s, i, score = Score(s) })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.i) // prefer earlier on the page
            .ToList();

        string? pick = ranked.Count > 0 ? ranked[0].s : null;

        // If top candidate looks like it continues on the next line, join them
        if (pick != null)
        {
            int idx = ranked[0].i;
            if (idx + 1 < lines.Count)
            {
                string next = lines[idx + 1];
                int letters = next.Count(char.IsLetter);
                int upper = next.Count(char.IsUpper);
                float upperRatio = letters == 0 ? 0f : (float)upper / letters;

                if (!blacklist.IsMatch(next) && upperRatio >= 0.55f && (pick.Length + 1 + next.Length) <= 180)
                {
                    pick = $"{pick} {next}";
                }
            }
        }

        // --- post-process to trim header noise from the picked line ---

        if (string.IsNullOrWhiteSpace(pick)) return null;
        // 1) Cut anything after common separators (authors, supervisor, submission text)
        var cut = Regex.Split(pick, @"\b(BY|SUPERVISOR|SUBMITTED|SUBMISSION|NAME OF|SIGNATURE|APPROVAL)\b",
                              RegexOptions.IgnoreCase)[0];

        // 2) If there’s an institutional prelude, start from the first likely title keyword
        var m = Regex.Match(cut,
            @"\b(FINAL\s+YEAR|PROJECT\s+REPORT|THESIS|DISSERTATION|RESEARCH\s+PROJECT|REPORT\s+ON)\b",
            RegexOptions.IgnoreCase);
        if (m.Success)
            cut = cut.Substring(m.Index);

        // 3) Clean spacing
        cut = Regex.Replace(cut, @"\s{2,}", " ").Trim();

        // 4) Use trimmed value if it looks reasonable
        if (cut.Length >= 15 && cut.Length <= 200)
            pick = cut;

        // --- end post-process ---



        return pick;


    }
}



public enum DocType
{
    AcademicResearch,
    BusinessCase,
    LegalCase,
    UnsupportedOther
}

public record DocTypeResult(
    DocType DocType,
    float Confidence,
    List<string> Signals,
    string Reason,
    List<(string label, float score)> Top2
);

public static class DocTypeClassifier
{
    // How many first pages to sample for signals
    const int MAX_PAGES = 8;
    const float MIN_SCORE_TO_ACCEPT = 5.0f;  // absolute floor
    const float MIN_CONFIDENCE = 0.55f;      // softmax prob floor

    public static DocTypeResult Evaluate(IEnumerable<dynamic> chunks)
    {
        // We only need Page + Preview; your chunks have x.Page and x.Preview
        var first = chunks
            .Where(x => (int)x.Page >= 1 && (int)x.Page <= MAX_PAGES)
            .OrderBy(x => (int)x.Page)
            .Take(250) // safety cap
            .Select(x => $"{x.Preview}\n")
            .ToList();

        var sample = string.Join("\n", first);
        var sampleLower = sample.ToLowerInvariant();

        // Quick guards for extremely short docs
        if (string.IsNullOrWhiteSpace(sample) || sample.Length < 400)
        {
            return Unsupported("Very short/empty front matter.");
        }

        // ----- Patterns & weights -----
        // Academic signals
        // Academic signals (broadened to cover CS, social science, econ, etc.)
        var academic = new (string pat, int w)[]
        {
            // Core scholarly structure
            (@"\babstract\b", 3),
            (@"\bkeywords?\b", 2),
            (@"\b(introduction|background)\b", 2),
            (@"\bmethods?\b|\bmethodology\b|\bmaterials?\s+and\s+methods?\b", 3),
            (@"\b(data\s+and\s+methods?|empirical\s+strategy)\b", 3),
            (@"\b(results?|findings?)\b", 3),
            (@"\bexperiments?\b|\bevaluation(s)?\b", 2),

            // Common modeling / CS / quant research cues
            (@"\bmodel\b", 1),
            (@"\barchitecture\b", 1),
            (@"\bneural\s+network(s)?\b|\btransformer(s)?\b", 1),
            (@"\bdataset(s)?\b|\bdata\s+set(s)?\b", 1),
            (@"\btraining\b|\bvalidation\b|\btest\s+set\b", 1),
            (@"\baccuracy\b|\bprecision\b|\brecall\b|\bf1[-\s]?score\b|\brmse\b|\bmse\b|\bauroc\b", 1),

            // Discourse / wrap-up
            (@"\bdiscussion\b", 3),
            (@"\bconclusions?\b|\blimitations?\b|\bfuture\s+work\b", 2),

            // Back matter & citations
            (@"\breferences\b|\bbibliography\b|\bworks\s+cited\b", 3),
            (@"doi:\s*\S+", 2),
            (@"arxiv:\s*\S+", 2),
            (@"\([A-Z][A-Za-z\-]+,\s*20\d{2}\)", 2), // APA-style cites
            (@"\[\d+\]", 1),                         // numeric cites

            // Publishing / authorship
            (@"\breceived\b.*\baccepted\b", 1),
            (@"\baffiliations?\b|\bcorresponding\s+author\b", 1),
        };


        // Business case signals
        var business = new (string pat, int w)[]
        {
            (@"\bexhibit\s+\d+\b", 3),
            (@"\b(teaching\s+note|learning\s+objectives?)\b", 3),
            (@"\bcase\s+questions?\b|\bdiscussion\s+questions?\b", 2),
            (@"\b(alternatives?|options?)\b", 2), (@"\brecommendation(s)?\b", 3),
            (@"\b(company\s+overview|background)\b", 2),
            (@"\bas of\s+\w+\s+\d{4}\b", 2),
            (@"\bmarket(s)?\b|\brevenue\b|\bcost(s)?\b|\bprofit(s)?\b", 1),
            (@"\byou are\b.*(manager|ceo|analyst|consultant)", 1)
        };

        // Legal case signals
        var legal = new (string pat, int w)[]
        {
            (@"\bv\.\b", 4), // X v. Y
            (@"\b(plaintiff|defendant|appellant|appellee)\b", 3),
            (@"\bfacts\b", 3), (@"\bissues?\b", 3), (@"\brule\b", 3),
            (@"\bholding\b|\bdisposition\b", 3),
            (@"\breasoning\b|\banalysis\b", 2),
            // Reporter cites (very loose)
            (@"\b\d{1,3}\s+(u\.s\.|f\.3d|s\.ct\.|scc|n\.y\.s\.\d)\b", 2)
        };

        // Unsupported (things we want to block for Guided Mode)
        var unsupported = new (string pat, int w)[]
        {
            (@"\bcurriculum\s+vitae\b|\bcv\b\b", 3),
            (@"\bexperience\b", 2), (@"\beducation\b", 2),
            (@"\bskills?\b", 2), (@"\bprojects?\b", 2),
            (@"\bcertifications?\b", 2), (@"\blanguages?\b", 1),
            (@"\bagenda\b", 2),
            (@"\b(invoice|brochure|flyer)\b", 1)
        };

        int scoreAcademic = Score(sampleLower, academic);
        int scoreBusiness = Score(sampleLower, business);
        int scoreLegal = Score(sampleLower, legal);
        int scoreBlock = Score(sampleLower, unsupported);

        // If strong unsupported signals, prefer Unsupported
        if (scoreBlock >= 6 && scoreBlock >= Math.Max(scoreAcademic, Math.Max(scoreBusiness, scoreLegal)))
        {
            return Unsupported($"Unsupported patterns dominated (score={scoreBlock}).");
        }

        // Pick top category
        var raw = new List<(string label, int score)>
        {
            ("academic_research", scoreAcademic),
            ("business_case", scoreBusiness),
            ("legal_case", scoreLegal)
        }.OrderByDescending(x => x.score).ToList();

        var top = raw[0];
        var second = raw[1];

        // Softmax-like confidence
        float conf = SoftmaxTop(new float[] { scoreAcademic, scoreBusiness, scoreLegal });

        // Absolute + confidence thresholds
        if (top.score < MIN_SCORE_TO_ACCEPT || conf < MIN_CONFIDENCE)
        {
            return Unsupported($"Low separation or weak signals (top={top.label} score={top.score}, conf={conf:0.00}).");
        }

        var docType = top.label switch
        {
            "academic_research" => DocType.AcademicResearch,
            "business_case" => DocType.BusinessCase,
            "legal_case" => DocType.LegalCase,
            _ => DocType.UnsupportedOther
        };

        // Collect a few positive signals to explain
        var signals = new List<string>();
        if (scoreAcademic > 0) signals.Add($"Academic:{scoreAcademic}");
        if (scoreBusiness > 0) signals.Add($"Business:{scoreBusiness}");
        if (scoreLegal > 0) signals.Add($"Legal:{scoreLegal}");
        if (scoreBlock > 0) signals.Add($"Unsupported:{scoreBlock}");

        string reason = $"Top={top.label} (score {top.score}) over {second.label} (score {second.score}); conf={conf:0.00}.";

        return new DocTypeResult(
            docType,
            conf,
            signals,
            reason,
            raw.Take(2).Select(x => (x.label, (float)x.score)).ToList()
        );

        // ---- local helpers ----
        static int Score(string text, (string pat, int w)[] rules)
        {
            int s = 0;
            foreach (var (pat, w) in rules)
            {
                if (Regex.IsMatch(text, pat, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                    s += w;
            }
            return s;
        }

        static float SoftmaxTop(float[] xs)
        {
            // shift for numerical stability
            float max = xs.Max();
            var exps = xs.Select(v => MathF.Exp(v - max)).ToArray();
            float sum = exps.Sum();
            return sum == 0 ? 0f : exps.Max() / sum;
        }

        static DocTypeResult Unsupported(string why) =>
            new DocTypeResult(DocType.UnsupportedOther, 0.0f, new List<string>(), why, new List<(string, float)>());
    }
}

public static class DocTypePersistence
{
    public static void Save(Guid uploadId, IWebHostEnvironment env, DocTypeResult result)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", $"docclass-{uploadId}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    public static bool TryLoad(Guid uploadId, IWebHostEnvironment env, out DocTypeResult? result)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", $"docclass-{uploadId}.json");
        if (!File.Exists(path))
        {
            result = null;
            return false;
        }

        var json = File.ReadAllText(path);
        result = System.Text.Json.JsonSerializer.Deserialize<DocTypeResult>(json);
        return result != null;
    }
}


// === STEP 7A helpers: feature flag + universal Academic focus keywords ===
static class TutorGrounding
{
    public const bool ENABLE_ACADEMIC_GROUNDING = true;

    // Generic, discipline-agnostic signals for each Academic focus
    public static readonly Dictionary<string, string[]> FocusKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["methodology"] = new[] {
            "method", "methodology", "materials", "procedure", "participants", "dataset",
            "preprocess", "instrumentation", "apparatus", "implementation", "algorithm",
            "architecture", "training", "hyperparameter", "validation", "experimental setup"
        },
            ["findings"] = new[] { "result", "results", "findings", "experiments", "evaluation", "analysis", "table", "figure", "accuracy", "effect size" },
            ["theory"] = new[] { "theory", "framework", "model", "background", "literature", "hypothesis", "hypotheses" },
            ["discussion"] = new[] { "discussion", "implications", "interpretation", "practical implications" },
            ["limitations"] = new[] { "limitation", "limitations", "threats to validity", "bias", "generalizability" },
            ["conclusion"] = new[] { "conclusion", "summary", "future work", "concluding remarks" }
        };

    // === STEP 7A: universal page-finder for Academic focus ===
    static string[] HeaderHintsFor(string focus) => focus.ToLowerInvariant() switch
    {
        "methodology" => new[] {
        "method", "methodology", "materials & methods", "experimental setup",
        "implementation", "approach", "research design", "participants",
        "procedure", "instrumentation"
    },
        "findings" => new[] { "results", "findings", "experiments", "evaluation", "analysis", "results and discussion" },
        "theory" => new[] { "theory", "background", "literature", "framework", "hypotheses" },
        "discussion" => new[] { "discussion", "implications", "interpretation" },
        "limitations" => new[] { "limitation", "limitations", "threats to validity", "threats", "bias" },
        "conclusion" => new[] { "conclusion", "summary", "future work" },
        _ => Array.Empty<string>()
    };

    public static List<int> FindPagesAcademic(string uploadId, string focus, int take = 3)
    {
        var path = Path.Combine("uploads", $"{uploadId}.index.json");
        if (!File.Exists(path)) return new();

        var kw = FocusKeywords.TryGetValue(focus ?? "", out var arr) ? arr : Array.Empty<string>();
        var headers = HeaderHintsFor(focus ?? "");

        var scores = new Dictionary<int, int>();

        using var stream = File.OpenRead(path);
        using var doc = System.Text.Json.JsonDocument.Parse(stream);

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            int page = el.TryGetProperty("Page", out var p) && p.TryGetInt32(out var pi) ? pi : -1;
            if (page < 1) continue;

            string text =
                (el.TryGetProperty("Text", out var tx) ? tx.GetString() : null)
                ?? (el.TryGetProperty("Preview", out var pv) ? pv.GetString() : null)
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text)) continue;

            int s = 0;
            foreach (var h in headers)
                if (text.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) s += 3;

            foreach (var k in kw)
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) s += 1;

            if (s > 0)
                scores[page] = (scores.TryGetValue(page, out var cur) ? cur : 0) + s;
        }

        if (scores.Count == 0) return new();

        // Take top pages, include ±1 neighbors for context
        var top = scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(take).ToList();
        var withNeighbors = new HashSet<int>(top);
        foreach (var pg in top) { withNeighbors.Add(pg - 1); withNeighbors.Add(pg + 1); }
        withNeighbors.RemoveWhere(x => x < 1);

        return withNeighbors.OrderBy(x => x).Take(take).ToList();
    }

    public static List<int> FindPagesBusiness(string uploadId, string focus, int take = 4)
    {
        var path = Path.Combine("uploads", $"{uploadId}.index.json");
        if (!File.Exists(path)) return new();

        var f = (focus ?? "").ToLowerInvariant();
        string[] keys = f switch
        {
            "problem" or "strategic context" => new[] { "problem", "background", "context", "objective", "goal", "scope" },
            "alternatives" or "options" => new[] { "alternative", "option", "scenario", "approach" },
            "analysis" => new[] { "analysis", "assumption", "sensitivity", "driver", "swot", "five forces" },
            "financials" => new[] { "$", "€", "£", "revenue", "cost", "profit", "npv", "irr", "roi", "cash flow", "%" },
            "risks" or "risks & delivery" => new[] { "risk", "mitigation", "uncertainty", "limitation", "trade-off" },
            "recommendation" or "next steps" => new[] { "recommendation", "conclusion", "next steps", "implementation", "roadmap" },
            _ => Array.Empty<string>()
        };

        var scores = new Dictionary<int, int>();

        using var stream = File.OpenRead(path);
        using var doc = System.Text.Json.JsonDocument.Parse(stream);

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            int page = el.TryGetProperty("Page", out var p) && p.TryGetInt32(out var pi) ? pi : -1;
            if (page < 1) continue;

            string text =
                (el.TryGetProperty("Text", out var tx) ? tx.GetString() : null)
                ?? (el.TryGetProperty("Preview", out var pv) ? pv.GetString() : null)
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text)) continue;

            int s = 0;
            foreach (var k in keys)
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) s += 1;

            // Small boost for numeric density when focus=financials
            if (f == "financials")
            {
                int digits = 0;
                foreach (var ch in text) if (char.IsDigit(ch)) digits++;
                if (digits > 80) s += 1;
            }

            if (s > 0)
                scores[page] = (scores.TryGetValue(page, out var cur) ? cur : 0) + s;
        }

        if (scores.Count == 0) return new();

        // Take top pages, include ±1 neighbors for context
        var top = scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(take).ToList();
        var withNeighbors = new HashSet<int>(top);
        foreach (var pg in top) { withNeighbors.Add(pg - 1); withNeighbors.Add(pg + 1); }
        withNeighbors.RemoveWhere(x => x < 1);

        return withNeighbors.OrderBy(x => x).Take(Math.Max(take, 3)).ToList();
    }


    // Build grounded UI for Academic steps (3 narrative lines + 3 two-line choices, all with [p:X])
    public static object BuildAcademicUI(string focus, int stepIndex, List<int> pages)
    {
        if (pages == null || pages.Count == 0)
        {
            // Thin context: we still respect the "every line has a chip" contract.
            // Use a conservative anchor (page 1) so this message is never ungrounded.
            var fallbackPages = new[] { 1 };

            var narrativeRaw =
                $"This topic appears complete for **{focus}** based on the pages reviewed.\n" +
                "Consider exploring another topic or exporting what has been covered.";

            var narrativeChipped = ChipUtil.ChipAllLines(narrativeRaw, fallbackPages);

            return new
            {
                narrative = narrativeChipped
,
                choices = new object[]
                {
                new {
                    id = "topics",
                    label = "Explore another topic.\nReturn to the topic list and choose a different angle. [p:1]"
                },
                new {
                    id = "change-focus",
                    label = "Change focus.\nSwitch to a different section such as Results or Discussion. [p:1]"
                },
                new {
                    id = "export",
                    label = "Export summary.\nSave the visited pages and brief notes for this path. [p:1]"
                }
                },
                cites = fallbackPages,
                stepSummary = $"Wrapped {focus} (thin context)"
            };
        }


        int p(int i) => pages[i % pages.Count];

        string narrative = focus.Equals("methodology", StringComparison.OrdinalIgnoreCase)
            ? $"Locate the methods section and note how data/procedures are introduced. [p:{p(0)}]\n" +
              $"Review preprocessing/setup that prepares inputs for the model or analysis. [p:{p(1)}]\n" +
              $"Check where the model/analysis configuration is described. [p:{p(2)}]"
            : $"Review the key section for {focus}. [p:{p(0)}]\n" +
              $"Cross-check supporting details or exhibits. [p:{p(1)}]\n" +
              $"Capture the intended takeaway. [p:{p(2)}]";

        var choices = new object[] {
        new { id = "drill",
              label = $"Drill into the most relevant paragraph(s) and extract exact anchors. [p:{p(0)}]\n" +
                      $"Leave with a concise, citable note. [p:{p(1)}]" },
        new { id = "contrast",
              label = $"Contrast this section with nearby pages for scope/consistency. [p:{p(1)}]\n" +
                      $"List any gaps deferred to later parts. [p:{p(2)}]" },
        new { id = "trace",
              label = $"Trace claim → evidence/procedure links in this section. [p:{p(2)}]\n" +
                      $"Map the chain so you can cite without guessing. [p:{p(0)}]" }
    };

        return new
        {
            narrative,
            choices,
            cites = pages.OrderBy(x => x).ToArray(),
            stepSummary = $"Grounded {focus}: step {stepIndex}"
        };
    }


    public static List<(int page, string text)> ExtractAnchorsFromPages(
    string uploadId,
    IEnumerable<int> pages,
    int maxTotal = 3,
    int maxChars = 220)
    {
        var anchors = new List<(int page, string text)>();
        try
        {
            var path = Path.Combine("uploads", $"{uploadId}.pdf");
            if (!File.Exists(path)) return anchors;

            using var doc = PdfPigDoc.Open(path);

            foreach (var p in pages.Distinct().OrderBy(x => x))
            {
                if (p < 1 || p > doc.NumberOfPages) continue;

                var page = doc.GetPage(p);
                var raw = page.Text ?? string.Empty;
                var snippet = TakeFirstSentences(raw, maxChars);
                if (string.IsNullOrWhiteSpace(snippet)) continue;

                anchors.Add((p, snippet));
                if (anchors.Count >= maxTotal) break;
            }
        }
        catch
        {
            // swallow — return whatever we could extract
        }

        return anchors;
    }

    static string TakeFirstSentences(string text, int maxChars)
    {
        var clean = CollapseWhitespace(text);
        if (clean.Length <= maxChars) return clean;

        // Try to end on a sentence boundary near the limit
        var softLimit = Math.Max(100, Math.Min(maxChars, clean.Length));
        var dot = clean.IndexOf('.', softLimit);
        var cut = dot > 0 && dot < softLimit + 120 ? dot + 1 : maxChars;
        if (cut > clean.Length) cut = clean.Length;

        return clean.Substring(0, cut).Trim() + "…";
    }

    static string CollapseWhitespace(string s)
    {
        return System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
    }


}

internal static class ChipUtil
{
    private static readonly Regex ChipRegex = new(@"\[p:\d+\]\s*$", RegexOptions.Compiled);

    public static string WithChip(string line, IReadOnlyList<int>? cites)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;
        if (ChipRegex.IsMatch(line)) return line;
        var page = (cites != null && cites.Count > 0) ? cites[^1] : 1; // ^1 = last item
        return $"{line.TrimEnd()} [p:{page}]";
    }

    public static string ChipAllLines(string? narrative, IReadOnlyList<int>? cites)
    {
        if (string.IsNullOrEmpty(narrative)) return narrative ?? "";
        var parts = narrative.Split('\n');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = WithChip(parts[i], cites);
        return string.Join("\n", parts);
    }
}
internal static class StreakUtil
{
    public static bool SameCites(IReadOnlyList<int>? a, IReadOnlyList<int>? b)
    {
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}

internal static class StepResponder
{
    public static IResult ReturnStep(
        TutorSession session,
        IReadOnlyList<int> cites,
        object ui)
    {
        // compare against previous cites
        if (StreakUtil.SameCites(cites, session.LastCites))
        {
            session = session with { NoNewCitesStreak = session.NoNewCitesStreak + 1 };
        }
        else
        {
            session = session with { NoNewCitesStreak = 0, LastCites = cites.ToList() };
        }

        TutorStore.Sessions[session.SessionId] = session;

        return Results.Json(new { sessionId = session.SessionId, state = session, ui, payload = ui });
    }
}






static class TutorHelpers
{
    public static string FocusDisplay(string? f) => f switch
    {
        "findings" => "Findings",
        "methodology" => "Methodology",
        "theory" => "Theory",
        "discussion" => "Discussion",
        "limitations" => "Limitations",
        "conclusion" => "Conclusion",
        _ => "Section"
    };
}




