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




const string JwtSecret = "samnii_JWT_secret_key_2025_super_strong_01_long_xyz";
const string JwtIssuer = "IngestionApi";
const string JwtAudience = "IngestionClient";







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
        .WithOrigins("http://localhost:5174", "http://localhost:3000", "http://localhost:4173", "https://ai-case-learning-assistant.vercel.app", "https://ai-case-learning-assistant-rku540uom.vercel.app")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});




var app = builder.Build();
app.UseCors("FrontendDev");
// app.UseHttpsRedirection();


// Choose a writable folder for SQLite (works on Windows + Azure)
var home = Environment.GetEnvironmentVariable("HOME")
           ?? Environment.GetEnvironmentVariable("USERPROFILE")
           ?? ".";
var dataDir = Path.Combine(home, "ingestion-data");
Directory.CreateDirectory(dataDir);

var dbPath = Path.Combine(dataDir, "ingestion.db");
var connString = $"Data Source={dbPath};Cache=Shared";

using (var conn = new SqliteConnection(connString))
{
    conn.Open();

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

CREATE TABLE IF NOT EXISTS Classes (
  Id TEXT PRIMARY KEY,
  InstructorId TEXT NOT NULL,
  Name TEXT NOT NULL,
  Description TEXT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ClassStudents (
  ClassId TEXT NOT NULL,
  StudentId TEXT NOT NULL,
  AddedAt TEXT NOT NULL,
  PRIMARY KEY (ClassId, StudentId)
);

CREATE TABLE IF NOT EXISTS ClassCases (
  ClassId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  AssignedAt TEXT NOT NULL,
  PRIMARY KEY (ClassId, UploadId)
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


    // 5) Add ClassId to Sessions (link sessions to classes)
    try
    {
        using var mig4 = conn.CreateCommand();
        mig4.CommandText = "ALTER TABLE Sessions ADD COLUMN ClassId TEXT NULL";
        mig4.ExecuteNonQuery();
    }
    catch
    {
        // Column already exists -> ignore
    }

   



    cmd.ExecuteNonQuery();
}


// --- JWT auth gate (protect everything except /ping and /auth/*) ---
var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "/ping",
    "/debug/db-sanity"
};


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

    // Use the hard-coded JWT constants so the key is long enough everywhere
    var secret = JwtSecret;
    var issuer = JwtIssuer;
    var audience = JwtAudience;

    try
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var claims = handler.ValidateToken(
            token,
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey =
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                // Allow small client/server clock differences (mobile devices, Azure infra)
                ClockSkew = TimeSpan.FromMinutes(5),
            },
            out var validatedToken);


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


bool IsInstructor(HttpContext ctx)
{
    return ctx.Items.TryGetValue("isSuperUser", out var val)
        && val is bool isSuper
        && isSuper == true;
}

bool IsStudent(HttpContext ctx)
{
    // Student = logged in but NOT instructor
    return ctx.Items.TryGetValue("isSuperUser", out var val)
        && val is bool isSuper
        && isSuper == false;
}

IResult RequireInstructor(HttpContext ctx)
{
    if (!IsInstructor(ctx))
        return Results.Forbid();

    return null;
}

IResult RequireStudent(HttpContext ctx)
{
    if (!IsStudent(ctx))
        return Results.Forbid();

    return null;
}





app.MapGet("/ping", () => Results.Ok("pong"));


// --- Auth: signup (create user) ---
app.MapPost("/auth/signup", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    string email = "", password = "", fullName = "";
    bool isInstructor = false; // NEW: drives IsSuperUser

    try
    {
        var obj = System.Text.Json.JsonDocument.Parse(body).RootElement;
        if (obj.TryGetProperty("email", out var e))
            email = (e.GetString() ?? "").Trim().ToLowerInvariant();

        if (obj.TryGetProperty("password", out var p))
            password = p.GetString() ?? "";

        if (obj.TryGetProperty("fullName", out var n))
            fullName = (n.GetString() ?? "").Trim();

        // NEW: optional flag from frontend
        // Frontend: send { ..., "isInstructor": true } if they chose Instructor
        if (obj.TryGetProperty("isInstructor", out var inst))
        {
            try
            {
                isInstructor = inst.GetBoolean();
            }
            catch
            {
                // invalid type? treat as false
                isInstructor = false;
            }
        }
    }
    catch
    {
        // bad JSON → will fail validation below
    }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest(new { error = "email and password required" });

    if (password.Length < 8)
        return Results.BadRequest(new { error = "password must be at least 8 characters" });

    var userId = Guid.NewGuid().ToString("N");
    var hash = BCrypt.Net.BCrypt.HashPassword(password);

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    // Enforce unique email
    var check = conn.CreateCommand();
    check.CommandText = "SELECT 1 FROM Users WHERE Email = $e LIMIT 1";
    check.Parameters.AddWithValue("$e", email);
    var exists = (await check.ExecuteScalarAsync()) != null;
    if (exists)
        return Results.Conflict(new { error = "email already exists" });

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO Users (Id, Email, PasswordHash, FullName, CreatedAt, IsSuperUser)
        VALUES ($id,$e,$h,$n,$t,$su)";
    cmd.Parameters.AddWithValue("$id", userId);
    cmd.Parameters.AddWithValue("$e", email);
    cmd.Parameters.AddWithValue("$h", hash);
    cmd.Parameters.AddWithValue("$n", string.IsNullOrWhiteSpace(fullName) ? DBNull.Value : fullName);
    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
    cmd.Parameters.AddWithValue("$su", isInstructor ? 1 : 0); // NEW: instructor ⇒ superuser

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { userId, email, fullName });

});


app.MapPost("/auth/login", async (HttpContext ctx) =>
{
    // ⭐ Allow body to be read multiple times
    ctx.Request.EnableBuffering();
    ctx.Request.Body.Position = 0;

    using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
    var body = await reader.ReadToEndAsync();

    // ⭐ Reset again so downstream can read body if needed
    ctx.Request.Body.Position = 0;

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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    string? userId = null, hash = null, fullName = null;
    bool isSuperUser = false;
    int rawIsSuperUser = -999;

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
            rawIsSuperUser = r.GetInt32(3);
            isSuperUser = rawIsSuperUser != 0;
        }
    }

    if (email == "timothywong@gmail.com")
        isSuperUser = true;

    Console.WriteLine($"[LOGIN DEBUG] email={email}, rawIsSuperUser={rawIsSuperUser}, isSuperUserBool={isSuperUser}");

    if (userId is null || hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
        return Results.Unauthorized();

    var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
        System.Text.Encoding.UTF8.GetBytes(JwtSecret));

    var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
        key,
        Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
    );

    var claims = new[]
    {
        new System.Security.Claims.Claim("sub", userId),
        new System.Security.Claims.Claim("email", email),
        new System.Security.Claims.Claim("isSuperUser", isSuperUser ? "true" : "false"),
    };

    var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        issuer: JwtIssuer,
        audience: JwtAudience,
        claims: claims,
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddMinutes(60),
        signingCredentials: creds
    );

    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
        .WriteToken(token);

    return Results.Ok(new { token = jwt, userId, email, fullName, isSuperUser });
});


app.MapGet("/me", async (HttpContext ctx) =>
{
    var userId = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    bool tokenIsSuper =
        ctx.Items.TryGetValue("isSuperUser", out var isSuperObj) &&
        isSuperObj is bool b && b;

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT Email,
               IFNULL(FullName, ''),
               IFNULL(IsSuperUser, 0)
        FROM Users
        WHERE Id = $id
        LIMIT 1;";
    cmd.Parameters.AddWithValue("$id", userId);

    using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync())
    {
        // Token might be valid but user row missing; treat as unauthorized
        return Results.Unauthorized();
    }

    var email = r.GetString(0);
    var fullName = r.GetString(1);
    var dbIsSuper = r.GetInt32(2) != 0;


    var isSuperUser = dbIsSuper || tokenIsSuper;


    var role = isSuperUser ? "instructor" : "student";

    return Results.Ok(new
    {
        userId,
        email,
        fullName,
        role
    });
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
app.MapGet("/uploads/{uploadId:guid}/summary", async (Guid uploadId, IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.summary.json");
    if (!File.Exists(path)) return Results.NotFound();
    var json = await File.ReadAllTextAsync(path);
    return Results.Text(json, "application/json");
});

// GET /cases — per-user list of uploads
app.MapGet("/cases", async (HttpContext ctx, IWebHostEnvironment env) =>
{
    // 1) Get current userId from JWT middleware
    var userId = ctx.Items["userId"] as string;
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
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString))
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

            foreach (var c in TextChunking.ChunkBySentences(text, 1200, 200))
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
            score = QaRetrieval.SafeCosine(qVec.Span, x.Vec.Span)

        })
        .OrderByDescending(s => s.score)
        .Take(5)
        .ToList();

    return Results.Json(scored);
});

app.MapGet("/debug/student-access/{uploadId}", async (Guid uploadId, HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
SELECT
    u.UploadId AS UploadId,
    u.UserId  AS OwnerId,
    cs.StudentId,
    cs.ClassId,
    cc.ClassId AS CaseClassId
FROM Uploads u
LEFT JOIN ClassCases cc ON cc.UploadId = u.UploadId
LEFT JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
WHERE u.UploadId = $u;
";
    cmd.Parameters.AddWithValue("$u", uploadId.ToString());

    var rows = new List<object>();
    using var r = await cmd.ExecuteReaderAsync();
    while (r.Read())
    {
        rows.Add(new
        {
            uploadId = r["UploadId"]?.ToString(),
            owner = r["OwnerId"]?.ToString(),
            studentId = r["StudentId"]?.ToString(),
            classId = r["ClassId"]?.ToString(),
            caseClass = r["CaseClassId"]?.ToString()
        });
    }

    return Results.Json(rows);
});

// GET /ask/{uploadId}?q=...
app.MapGet("/ask/{uploadId:guid}", async (Guid uploadId, string q, string? sessionId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = (string?)ctx.Items["userId"] ?? "";
    Console.WriteLine($"[ASK DEBUG] me={me}, uploadId={uploadId}");

    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString))
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE upper(u.UploadId) = upper($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE cc.UploadId = u.UploadId
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";


        chk.Parameters.AddWithValue("$u", uploadId.ToString());
        chk.Parameters.AddWithValue("$me", me);
        var ok = await chk.ExecuteScalarAsync();

        if (ok is null)
            return Results.NotFound(new { error = "not found" });

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

        using var mconn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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


    string? GetStringOrNull(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    int[]? ParseNullableIntArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<int[]>(json);
        }
        catch
        {
            return null;
        }
    }

    try
    {
        // --- record USER message (if a session was provided) ---
        SaveMessage("user", q, null, null);

        // --- Q/A CACHE FAST PATH ---
        // If we've seen this exact question for this upload before,
        // reuse the previous answer instead of redoing retrieval + LLM.

        if (!string.IsNullOrWhiteSpace(q))
        {
            try
            {
                using var cacheConn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                cacheConn.Open();

                using var cacheCmd = cacheConn.CreateCommand();
                cacheCmd.CommandText = @"
SELECT a.Content, a.Citations, a.PagesUsed
FROM Sessions s
JOIN Messages qMsg
    ON qMsg.SessionId = s.Id
   AND qMsg.Role = 'user'
JOIN Messages a
    ON a.SessionId = s.Id
   AND a.Role = 'assistant'
   AND a.CreatedAt >= qMsg.CreatedAt
WHERE s.UploadId = $uploadId
  AND LOWER(TRIM(qMsg.Content)) = LOWER(TRIM($q))
ORDER BY a.CreatedAt ASC
LIMIT 1;
";
                cacheCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());
                cacheCmd.Parameters.AddWithValue("$q", q.Trim());

                using var r = cacheCmd.ExecuteReader();
                if (r.Read())
                {
                    var answerText = r.GetString(0);
                    var citations = ParseNullableIntArray(GetStringOrNull(r, 1));
                    var pagesUsed = ParseNullableIntArray(GetStringOrNull(r, 2));

                    // Still write this assistant message into the current session history
                    SaveMessage("assistant", answerText, citations, pagesUsed);

                    return Results.Json(new
                    {
                        answer = answerText,
                        citations = citations ?? Array.Empty<int>(),
                        pagesUsed = pagesUsed ?? Array.Empty<int>(),
                        fromCache = true
                    });
                }
            }
            catch
            {
                // Swallow cache errors so they never break the main Q/A path.
                // If cache lookup fails, we just continue as normal.
            }
        }
        // --- END Q/A CACHE FAST PATH ---


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
        qNorm = Regex.Replace(
    qNorm,
    @"\b(results?|experimental\s+results?|outcomes?|observations?|measurements?|future\s+work|recommendations?|improvements?)\b",
    "conclusion",
    RegexOptions.IgnoreCase
);

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

        // 🔹 Phase 3: boost method / findings pages into the context
        var sectionHints = new List<TopChunk>();

        // If question is about methods/data collection → pull method-like sections
        if (questionType == QuestionType.Method)
        {
            sectionHints = SectionSwitchboard.FindMethodLikeSections(list);
        }
        // If question is about findings / conclusions / "why" → pull results/discussion
        else if (questionType == QuestionType.Findings || questionType == QuestionType.WhyExplain)
        {
            sectionHints = SectionSwitchboard.FindFindingsLikeSections(list);
        }

        if (sectionHints.Count > 0)
        {
            var existingPages = new HashSet<int>(top.Select(t => t.Page));
            foreach (var hint in sectionHints)
            {
                if (!existingPages.Contains(hint.Page))
                {
                    top.Add(hint);
                    existingPages.Add(hint.Page);
                }
            }
        }


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
        // Replace the threshold logic in your /ask endpoint (around line 1100-1200)
        // Find this section and replace it:

        // IMPROVED: More adaptive thresholds based on question type
        var isSummary = questionType == QuestionType.Summary;
        var isFact = questionType == QuestionType.Fact;
        var isMethod = questionType == QuestionType.Method;
        var isFindings = questionType == QuestionType.Findings;
        var isWhyExplain = questionType == QuestionType.WhyExplain;

        // IMPROVED: Adaptive breadth with better defaults
        bool vague = isSummary
            || isFindings
            || isWhyExplain
            || QaRetrieval.IsListy(qNorm)
            || intent == SectionIntent.Abstract
            || intent == SectionIntent.Conclusion
            || Regex.IsMatch(qNorm, @"\b(key\s+findings?|takeaways?|insights?|overview|summary|summari[sz]e|tl;dr)\b",
                             RegexOptions.IgnoreCase);

        // IMPROVED: More generous K values
        int desiredK;
        if (isSummary)
            desiredK = 25;         // very broad context (increased from 20)
        else if (isFindings)
            desiredK = 18;         // results-type questions (increased from 14)
        else if (isMethod)
            desiredK = 14;         // methods questions (increased from 10)
        else if (isFact)
            desiredK = 10;         // narrow, precise (increased from 6)
        else if (vague)
            desiredK = 16;         // generic vague / listy (increased from 12)
        else
            desiredK = 12;         // default (increased from 8)

        // Always try to get at least desiredK chunks
        if (top.Count < desiredK)
        {
            var fbWider = QaRetrieval.KeywordFallback(list, qNorm, k: desiredK * 2); // get even more
            if (fbWider.Count > top.Count)
            {
                // Merge the results, keeping best scores
                var merged = top.Concat(fbWider)
                    .GroupBy(t => $"{t.Page}_{t.Preview}")
                    .Select(g => g.OrderByDescending(x => x.Score).First())
                    .OrderByDescending(t => t.Score)
                    .Take(desiredK)
                    .ToList();
                top = merged;
            }
        }

        var lowIntent = intent is SectionIntent.Abstract or SectionIntent.Introduction
              or SectionIntent.Conclusion or SectionIntent.References
              or SectionIntent.Keywords or SectionIntent.Authors
              or SectionIntent.Title;

        // IMPROVED: Much more lenient thresholds
        var THRESHOLD = intent == SectionIntent.Title
            ? 0.95f   // slightly relaxed from 0.99f
            : isFact
                ? 0.08f   // relaxed from 0.15f
                : (lowIntent || isSummary ? 0.00f : 0.05f); // relaxed from 0.10f

        var bestScore = top.Count > 0 ? top.Max(t => t.Score) : 0f;
        var pageSpread = top.Select(t => t.Page).Distinct().Count();

        // IMPROVED: More aggressive threshold lowering
        if (bestScore < THRESHOLD && pageSpread >= 2) // reduced from 3
        {
            THRESHOLD = Math.Max(0.02f, THRESHOLD - 0.08f); // bigger drop
        }

        // IMPROVED: Even if below threshold, try keyword fallback before giving up
        if (top.Count == 0 || bestScore < THRESHOLD)
        {
            // Always try keyword fallback for non-title queries
            if (intent != SectionIntent.Title && intent != SectionIntent.Authors)
            {
                var fb = QaRetrieval.KeywordFallback(list, qNorm, k: desiredK * 2);
                if (fb.Count > 0)
                {
                    // Use keyword fallback results
                    top = fb;
                    bestScore = fb.Max(t => t.Score);
                }
            }

            // Handle Title/Authors metadata separately (keep your existing logic)
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
                            citations = Array.Empty<int>()
                        });
                    }
                }
            }

            // ONLY give up if we have NO results after all attempts
            if (top.Count == 0)
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
                    debug = new { bestScore, threshold = THRESHOLD, retrievedChunks = top.Count }
                });
            }
        }

        // Continue with normal flow using 'top' results
        var stitchedTop = ContextStitching.ExpandWithNeighbors(list, top,
            sideNeighbors: techGroup ? 3 : 2,           // increased
            maxTotalNeighbors: techGroup ? 15 : 12);     // increased
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

        // Find this section in your /ask endpoint (around line 1410-1425)
        // It's in the AnswerWithContext function
        // REPLACE the questionTypeHint assignment with this:

        var questionTypeHint = questionType switch
        {
            QuestionType.Summary =>
                "The user wants a high-level overview. Synthesize the main points from the Context.",

            QuestionType.Fact =>
                "The user wants specific facts. Extract precise information directly from the Context.",

            QuestionType.Method =>
                "The user is asking about methodology or experimental approach. " +
                "Look for ANY information in the Context about:\n" +
                "- How the study/work was conducted\n" +
                "- What methods, techniques, or procedures were used\n" +
                "- How data was collected or analyzed\n" +
                "- Sample information, participants, or datasets\n" +
                "- Experimental design or setup\n" +
                "Even if the Context doesn't have a 'Methods' section, look for method-related " +
                "information ANYWHERE in the provided text and synthesize it into a clear answer.",

            QuestionType.Findings =>
                "The user wants to know the results or findings. " +
                "Look for outcomes, measurements, observations, and key results in the Context.",

            QuestionType.WhyExplain =>
                "The user wants an explanation or rationale. " +
                "Use the Context to explain the reasoning, causes, or implications.",

            _ => string.Empty
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
      "You are a precise but helpful assistant analyzing a PDF document. " +
      "Your PRIMARY task is to answer the user's question using the provided Context. " +
      "\n\n" +
      "CORE PRINCIPLES:\n" +
      "1. The Context contains relevant excerpts from the document. Use it as your source.\n" +
      "2. If the answer is clearly in the Context, provide it confidently.\n" +
      "3. For specific factual questions, extract precise details from the Context.\n" +
      "4. For general questions (summaries, overviews, main points), synthesize across the Context.\n" +
      "5. You may infer reasonable connections between ideas in the Context.\n" +
      "6. ONLY say 'I can't find that' if the Context truly has NO relevant information.\n" +
      "\n\n" +
      "WHEN TO SAY YOU CAN'T FIND SOMETHING:\n" +
      "- The question asks for specific data (names, numbers, dates) that aren't in Context\n" +
      "- The question is about a topic completely absent from the Context\n" +
      "- DO NOT say you can't find it just because the answer requires synthesis\n" +
      "- DO NOT say you can't find it just because the exact phrasing isn't there\n" +
      "\n\n" +
      "CITATION RULES:\n" +
      "- End each factual claim or piece of information with [p:X] where X is the page number\n" +
      "- Use the page numbers shown in the Context (e.g., '— Page 5 —')\n" +
      "- If synthesizing from multiple pages, include multiple chips: [p:3] [p:5] [p:7]\n" +
      (string.IsNullOrWhiteSpace(categoryHint) ? "" : "\n" + categoryHint + "\n") +
      (string.IsNullOrWhiteSpace(questionTypeHint) ? "" : "\n" + questionTypeHint + "\n") +
      bulletRules +
      "\n\n" +
      "EXAMPLE RESPONSES:\n" +
      "\n" +
      "Context:\n" +
      "— Page 8 —\n" +
      "The experiment achieved 94.2% accuracy on the test set using a fine-tuned BERT-base model.\n" +
      "— Page 12 —\n" +
      "All experiments were run on NVIDIA A100 GPUs with batch size 32.\n" +
      "\n" +
      "Question: What accuracy did they report?\n" +
      "Good Answer: The experiment achieved 94.2% accuracy on the test set. [p:8]\n" +
      "\n" +
      "Question: What was their experimental setup?\n" +
      "Good Answer: They used a fine-tuned BERT-base model [p:8] and ran experiments on NVIDIA A100 GPUs with batch size 32. [p:12]\n" +
      "\n" +
      "Question: What datasets did they use?\n" +
      "Bad Answer: I can't find that in the document.\n" +
      "Why bad: You should look more carefully at the Context before giving up.\n" +
      "\n\n" +
      "Now answer the user's question using the Context provided below.";




        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.SystemChatMessage(sys),
            new OpenAI.Chat.UserChatMessage($@"Question: {question}

Context:
{ctxStr}")
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f
           

        };

        var result = chat.CompleteChat(messages, options).Value;

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
app.MapGet("/ask/stream/{uploadId}", async (string uploadId, string q, string? sessionId, HttpContext ctx, IWebHostEnvironment env) =>
{
    if (!Guid.TryParse(uploadId, out var parsedUploadId))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = "not found" });
        return;
    }


    var me = (string?)ctx.Items["userId"] ?? "";
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString))
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE u.UploadId = $u
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE cc.UploadId = u.UploadId
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
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
        if (!IndexPersistence.TryLoad(parsedUploadId, env, out list))
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

        using var mconn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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


    string? GetStringOrNull(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    int[]? ParseNullableIntArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<int[]>(json);
        }
        catch
        {
            return null;
        }
    }


    try
    {
        // --- record USER message at the start of the main happy path ---
        SaveMessage("user", q, null, null);

        if (!string.IsNullOrWhiteSpace(q))
        {
            using var cacheConn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
            await cacheConn.OpenAsync();

            using var cacheCmd = cacheConn.CreateCommand();
            cacheCmd.CommandText = @"
SELECT m.Content,
       m.Citations,
       m.PagesUsed
FROM Messages m
JOIN Sessions s ON s.Id = m.SessionId
WHERE s.UploadId = $u
  AND s.UserId   = $user
  AND m.Role     = 'assistant'
  AND EXISTS (
      SELECT 1 FROM Messages mu
      WHERE mu.SessionId = m.SessionId
        AND mu.Role      = 'user'
        AND lower(trim(mu.Content)) = lower(trim($q))
        AND mu.Id < m.Id
  )
ORDER BY m.Id DESC
LIMIT 1;
";
            cacheCmd.Parameters.AddWithValue("$u", uploadId.ToString());
            cacheCmd.Parameters.AddWithValue("$user", me);
            cacheCmd.Parameters.AddWithValue("$q", q.Trim());

            using var reader = await cacheCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var cachedAnswer = reader.GetString(0);
                var cachedPages = ParseNullableIntArray(GetStringOrNull(reader, 2));

                // Stream the cached answer as SSE:
                // 1) one token event with the full text
                await ctx.Response.WriteAsync(
                    $"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = cachedAnswer })}\n\n"
                );

                // 2) citations event (pages used)
                await ctx.Response.WriteAsync(
                    $"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(cachedPages)}\n\n"
                );

                // 3) persist this assistant message into history for this session too
                SaveMessage("assistant", cachedAnswer, cachedPages, cachedPages);

                // 4) done
                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }
        }

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
            var (metaTitle, _) = PdfMetadataHelper.Read(parsedUploadId, env);
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

            var guess = TitleHeuristics.FromPdfFirstPage(parsedUploadId, env);
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
            var (_, metaAuthor) = PdfMetadataHelper.Read(parsedUploadId, env);
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

        // 🔹 Phase 3: boost method / findings pages into the context
        var sectionHints = new List<TopChunk>();

        // If question is about methods/data collection → pull method-like sections
        if (questionType == QuestionType.Method)
        {
            sectionHints = SectionSwitchboard.FindMethodLikeSections(list);
        }
        // If question is about findings / conclusions / "why" → pull results/discussion
        else if (questionType == QuestionType.Findings || questionType == QuestionType.WhyExplain)
        {
            sectionHints = SectionSwitchboard.FindFindingsLikeSections(list);
        }

        if (sectionHints.Count > 0)
        {
            var existingPages = new HashSet<int>(top.Select(t => t.Page));
            foreach (var hint in sectionHints)
            {
                if (!existingPages.Contains(hint.Page))
                {
                    top.Add(hint);
                    existingPages.Add(hint.Page);
                }
            }
        }


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
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

// Resolve IWebHostEnvironment so we can find the uploads folder
var env = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>();
var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");


using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

        // 🔹 NEW: Try to override caseName using the summary JSON (same source as /cases)
        if (!string.IsNullOrWhiteSpace(uploadId))
        {
            var summaryPath = Path.Combine(uploadsRoot, $"{uploadId}.summary.json");
            if (File.Exists(summaryPath))
            {
                try
                {
                    using var fs = File.OpenRead(summaryPath);
                    using var summaryDoc = JsonDocument.Parse(fs);
                    var rootEl = summaryDoc.RootElement;

                    if (rootEl.TryGetProperty("fileName", out var fn) &&
                        fn.ValueKind == JsonValueKind.String)
                    {
                        var fromJson = fn.GetString();
                        if (!string.IsNullOrWhiteSpace(fromJson))
                        {
                            caseName = fromJson!;
                        }
                    }
                }
                catch
                {
                    // If the summary JSON is broken, keep the DB caseName ("Untitled case" or whatever)
                }
            }
        }

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
    string? uploadId = null;
    if (root.TryGetProperty("uploadId", out var u) && u.ValueKind == JsonValueKind.String)
    {
        var raw = u.GetString();
        uploadId = string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Trim().ToUpperInvariant();
    }


    var sessionId = Guid.NewGuid().ToString("N");
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
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





app.MapPost("/classes", async (HttpContext ctx) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (me == null) return Results.Unauthorized();

    var body = await ctx.Request.ReadFromJsonAsync<ClassCreateDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
    {
        return Results.BadRequest(new { error = "Missing class name" });
    }

    var id = Guid.NewGuid().ToString();
    var createdAt = DateTime.UtcNow.ToString("o");

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO Classes (Id, InstructorId, Name, Description, CreatedAt)
        VALUES ($id, $instructor, $name, $description, $createdAt);
    ";

    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$instructor", me);
    cmd.Parameters.AddWithValue("$name", body.Name);
    cmd.Parameters.AddWithValue("$description", body.Description ?? (object)DBNull.Value);
    cmd.Parameters.AddWithValue("$createdAt", createdAt);

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        id,
        name = body.Name,
        description = body.Description,
        instructorId = me,
        createdAt
    });
});


app.MapPost("/classes/{classId}/students", async (string classId, HttpContext ctx) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var body = await ctx.Request.ReadFromJsonAsync<AddStudentToClassDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.StudentEmail))
    {
        return Results.BadRequest(new { error = "Missing studentEmail" });
    }

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Check that the class exists and belongs to this instructor
    using (var checkClass = conn.CreateCommand())
    {
        checkClass.CommandText = @"
            SELECT COUNT(*) 
            FROM Classes 
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkClass.Parameters.AddWithValue("$classId", classId);
        checkClass.Parameters.AddWithValue("$instructorId", me);

        var count = (long)(await checkClass.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Find the student by email
    string? studentId = null;
    using (var findStudent = conn.CreateCommand())
    {
        findStudent.CommandText = @"
            SELECT Id 
            FROM Users 
            WHERE Email = $email;
        ";
        findStudent.Parameters.AddWithValue("$email", body.StudentEmail);

        var result = await findStudent.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
        {
            return Results.NotFound(new { error = "No user found with that email" });
        }

        studentId = (string)result;
    }

    // 3) Check if already in class
    using (var checkExisting = conn.CreateCommand())
    {
        checkExisting.CommandText = @"
            SELECT COUNT(*) 
            FROM ClassStudents
             WHERE ClassId = $classId AND StudentId = $studentId;

         ";
        checkExisting.Parameters.AddWithValue("$classId", classId);
        checkExisting.Parameters.AddWithValue("$studentId", studentId!);

        var exists = (long)(await checkExisting.ExecuteScalarAsync() ?? 0L);
        if (exists > 0)
        {
            return Results.Ok(new
            {
                classId,
                studentId,
                alreadyInClass = true
            });
        }
    }

    // 4) Insert into ClassStudents
    using (var insert = conn.CreateCommand())
    {
        insert.CommandText = @"
            INSERT INTO ClassStudents (ClassId, StudentId, AddedAt)
            VALUES ($classId, $studentId, $addedAt);
        ";
        insert.Parameters.AddWithValue("$classId", classId);
        insert.Parameters.AddWithValue("$studentId", studentId!);
        insert.Parameters.AddWithValue("$addedAt", DateTime.UtcNow.ToString("o"));

        await insert.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        classId,
        studentId,
        added = true
    });
});


app.MapPost("/classes/{classId}/cases", async (string classId, HttpContext ctx) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var body = await ctx.Request.ReadFromJsonAsync<AssignCaseToClassDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.UploadId))
    {
        return Results.BadRequest(new { error = "Missing uploadId" });
    }

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Check that the class exists and belongs to this instructor
    using (var checkClass = conn.CreateCommand())
    {
        checkClass.CommandText = @"
            SELECT COUNT(*) 
            FROM Classes 
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkClass.Parameters.AddWithValue("$classId", classId);
        checkClass.Parameters.AddWithValue("$instructorId", me);

        var count = (long)(await checkClass.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Check that the upload exists and belongs to this instructor
    var uploadId = body.UploadId.Trim().ToUpperInvariant();

    using (var checkUpload = conn.CreateCommand())
    {
        checkUpload.CommandText = @"
            SELECT COUNT(*)
            FROM Uploads
            WHERE UploadId = $uploadId AND UserId = $ownerId;

        ";
        checkUpload.Parameters.AddWithValue("$uploadId", uploadId!);
        checkUpload.Parameters.AddWithValue("$ownerId", me);

        var count = (long)(await checkUpload.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            return Results.NotFound(new { error = "Upload not found or not owned by you" });
        }
    }

    // 3) Check if this case is already assigned to the class
    using (var checkExisting = conn.CreateCommand())
    {
        checkExisting.CommandText = @"
            SELECT COUNT(*)
            FROM ClassCases
            WHERE ClassId = $classId AND UploadId = $uploadId;
        ";
        checkExisting.Parameters.AddWithValue("$classId", classId);
        checkExisting.Parameters.AddWithValue("$uploadId", uploadId!);

        var exists = (long)(await checkExisting.ExecuteScalarAsync() ?? 0L);
        if (exists > 0)
        {
            return Results.Ok(new
            {
                classId,
                uploadId,
                alreadyAssigned = true
            });
        }
    }

    // 4) Insert into ClassCases
    using (var insert = conn.CreateCommand())
    {
        insert.CommandText = @"
            INSERT INTO ClassCases (ClassId, UploadId, AssignedAt)
            VALUES ($classId, $uploadId, $assignedAt);
        ";
        insert.Parameters.AddWithValue("$classId", classId);
        insert.Parameters.AddWithValue("$uploadId", uploadId!);
        insert.Parameters.AddWithValue("$assignedAt", DateTime.UtcNow.ToString("o"));

        await insert.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        classId,
        uploadId,
        assigned = true
    });
});


app.MapGet("/classes/{classId}/details", async (string classId, HttpContext ctx) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Load class info and verify ownership
    string? className = null;
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT Name
            FROM Classes
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        cmd.Parameters.AddWithValue("$classId", classId);
        cmd.Parameters.AddWithValue("$instructorId", me);

        var result = await cmd.ExecuteScalarAsync();
        className = result as string;
        if (className is null)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Get students in the class
    var students = new List<object>();
    using (var stuCmd = conn.CreateCommand())
    {
        stuCmd.CommandText = @"
            SELECT Users.Id, Users.Email, Users.FullName
            FROM ClassStudents
            JOIN Users ON Users.Id = ClassStudents.StudentId
            WHERE ClassStudents.ClassId = $classId;
        ";
        stuCmd.Parameters.AddWithValue("$classId", classId);

        using var r = await stuCmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            students.Add(new
            {
                id = r.GetString(0),
                email = r.GetString(1),
                fullName = r.GetString(2)
            });
        }
    }

    // 3) Get assigned cases for the class
    var cases = new List<object>();
    using (var caseCmd = conn.CreateCommand())
    {
        caseCmd.CommandText = @"
            SELECT Uploads.UploadId, Uploads.OriginalFileName
            FROM ClassCases
            JOIN Uploads ON Uploads.UploadId = ClassCases.UploadId
            WHERE ClassCases.ClassId = $classId;
        ";
        caseCmd.Parameters.AddWithValue("$classId", classId);

        using var r = await caseCmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            cases.Add(new
            {
                uploadId = r.GetString(0),
                fileName = r.GetString(1)
            });
        }
    }

    return Results.Ok(new
    {
        classId,
        name = className,
        students,
        cases
    });
});


app.MapGet("/classes/{classId}/history", async (string classId, HttpContext ctx) =>
{
    // 0) Only instructors can call this
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    // Optional filters from query string
    var query = ctx.Request.Query;
    var studentId = query.ContainsKey("studentId") ? query["studentId"].ToString() : null;
    var uploadId = query.ContainsKey("uploadId") ? query["uploadId"].ToString() : null;

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Check that the class exists and belongs to this instructor
    using (var checkClass = conn.CreateCommand())
    {
        checkClass.CommandText = @"
            SELECT COUNT(*)
            FROM Classes
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkClass.Parameters.AddWithValue("$classId", classId);
        checkClass.Parameters.AddWithValue("$instructorId", me);

        var count = (long)(await checkClass.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Build query for class-scoped session summaries
    var cmd = conn.CreateCommand();
    var sql = @"
SELECT
    s.Id                           AS SessionId,
    s.UserId                       AS UserId,
    IFNULL(u.FullName, '')         AS UserFullName,
    IFNULL(u.Email, '')            AS UserEmail,
    s.UploadId                     AS UploadId,
    IFNULL(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt                    AS SessionCreatedAt,
    (
        SELECT COUNT(1)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS MessageCount,
    (
        SELECT Content
        FROM Messages m
        WHERE m.SessionId = s.Id
          AND m.Role = 'user'
        ORDER BY m.CreatedAt ASC
        LIMIT 1
    ) AS FirstUserQuestion
FROM Sessions s
JOIN ClassStudents cs
    ON cs.StudentId = s.UserId
   AND cs.ClassId = $classId
JOIN ClassCases cc
    ON cc.ClassId = cs.ClassId
    AND UPPER(cc.UploadId) = UPPER(s.UploadId)

LEFT JOIN Users   u  ON u.Id        = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
WHERE 1 = 1
";

    cmd.Parameters.AddWithValue("$classId", classId);

    if (!string.IsNullOrWhiteSpace(studentId))
    {
        sql += " AND s.UserId = $studentId";
        cmd.Parameters.AddWithValue("$studentId", studentId);
    }

    if (!string.IsNullOrWhiteSpace(uploadId))
    {
        sql += " AND s.UploadId = $uploadId";
        cmd.Parameters.AddWithValue("$uploadId", uploadId);
    }

    sql += " ORDER BY s.CreatedAt DESC;";
    cmd.CommandText = sql;

    var list = new List<object>();
    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            list.Add(new
            {
                sessionId = reader.GetString(0),
                studentId = reader.GetString(1),
                studentName = reader.GetString(2),
                studentEmail = reader.GetString(3),
                uploadId = reader.IsDBNull(4) ? null : reader.GetString(4),
                caseFileName = reader.IsDBNull(5) ? null : reader.GetString(5),
                startedAt = reader.GetString(6),
                messageCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                firstUserQuestion = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }
    }

    return Results.Ok(list);
});


app.MapGet("/sessions/{sessionId}/messages", async (string sessionId, HttpContext ctx) =>
{
    // Only instructors may view session logs
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var instructorId = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(instructorId))
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    //
    // 1) Load session metadata
    //
    var sessionCmd = conn.CreateCommand();
    sessionCmd.CommandText = @"
        SELECT s.UserId, s.UploadId, s.CreatedAt,
               u.FullName, u.Email, up.OriginalFileName
        FROM Sessions s
        LEFT JOIN Users u ON u.Id = s.UserId
        LEFT JOIN Uploads up ON up.UploadId = s.UploadId
        WHERE s.Id = $sessionId;
    ";
    sessionCmd.Parameters.AddWithValue("$sessionId", sessionId);

    string? studentId = null;
    string? uploadId = null;
    string? createdAt = null;
    string studentName = "";
    string studentEmail = "";
    string caseFileName = "";

    using (var r = await sessionCmd.ExecuteReaderAsync())
    {
        if (!await r.ReadAsync())
            return Results.NotFound(new { error = "Session not found" });

        studentId = r.GetString(0);
        uploadId = r.IsDBNull(1) ? null : r.GetString(1);
        createdAt = r.GetString(2);
        studentName = r.IsDBNull(3) ? "" : r.GetString(3);
        studentEmail = r.IsDBNull(4) ? "" : r.GetString(4);
        caseFileName = r.IsDBNull(5) ? "" : r.GetString(5);
    }

    //
    // 2) Ensure this session belongs to a class owned by the instructor
    //
    var checkCmd = conn.CreateCommand();
    checkCmd.CommandText = @"
        SELECT COUNT(*)
        FROM ClassStudents cs
        JOIN ClassCases cc ON cc.ClassId = cs.ClassId
        JOIN Classes c ON c.Id = cs.ClassId
        WHERE cs.StudentId = $studentId
          AND cc.UploadId = $uploadId
          AND c.InstructorId = $instructorId;
    ";
    checkCmd.Parameters.AddWithValue("$studentId", studentId);
    checkCmd.Parameters.AddWithValue("$uploadId", uploadId ?? "");
    checkCmd.Parameters.AddWithValue("$instructorId", instructorId);

    var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
    if (count == 0)
    {
        return Results.Forbid();
    }

    //
    // 3) Load messages
    //
    var msgCmd = conn.CreateCommand();
    msgCmd.CommandText = @"
        SELECT Role, Content, CreatedAt
        FROM Messages
        WHERE SessionId = $sessionId
        ORDER BY CreatedAt ASC;
    ";
    msgCmd.Parameters.AddWithValue("$sessionId", sessionId);

    var messages = new List<object>();
    using (var r = await msgCmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            messages.Add(new
            {
                role = r.GetString(0),
                content = r.GetString(1),
                timestamp = r.GetString(2)
            });
        }
    }

    // Final response:
    return Results.Ok(new
    {
        sessionId,
        studentId,
        studentName,
        studentEmail,
        uploadId,
        caseFileName,
        createdAt,
        messages
    });
});

app.MapPost("/sessions/start", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    string uploadId = "";
    string? classId = null;

    try
    {
        var obj = JsonDocument.Parse(body).RootElement;

        if (obj.TryGetProperty("uploadId", out var u))
            uploadId = u.GetString() ?? "";

        if (obj.TryGetProperty("classId", out var c))
            classId = string.IsNullOrWhiteSpace(c.GetString()) ? null : c.GetString();
    }
    catch { }

    if (string.IsNullOrWhiteSpace(uploadId))
        return Results.BadRequest(new { error = "uploadId required" });

    var userId = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var newSessionId = Guid.NewGuid().ToString("N");

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
INSERT INTO Sessions (Id, UserId, UploadId, ClassId, CreatedAt)
VALUES ($id, $user, $upload, $classId, $ts);
";

    cmd.Parameters.AddWithValue("$id", newSessionId);
    cmd.Parameters.AddWithValue("$user", userId);
    cmd.Parameters.AddWithValue("$upload", uploadId);
    cmd.Parameters.AddWithValue("$classId", (object?)classId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { sessionId = newSessionId });
});


app.MapGet("/classes/{classId}/students", async (string classId, HttpContext ctx) =>
{
    // Only instructors can view the students in a class
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Verify class belongs to this instructor
    using (var checkClass = conn.CreateCommand())
    {
        checkClass.CommandText = @"
            SELECT COUNT(*)
            FROM Classes
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkClass.Parameters.AddWithValue("$classId", classId);
        checkClass.Parameters.AddWithValue("$instructorId", me);

        var count = (long)(await checkClass.ExecuteScalarAsync() ?? 0L);
        if (count == 0)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Get all students in the class
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 
            cs.StudentId,
            cs.AddedAt,
            u.FullName,
            u.Email
        FROM ClassStudents cs
        JOIN Users u ON u.Id = cs.StudentId
        WHERE cs.ClassId = $classId
        ORDER BY u.FullName COLLATE NOCASE ASC;
    ";
    cmd.Parameters.AddWithValue("$classId", classId);

    var list = new List<object>();

    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var studentId = reader.GetString(0);
            var addedAt = reader.IsDBNull(1) ? null : reader.GetString(1);
            var fullName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var email = reader.IsDBNull(3) ? "" : reader.GetString(3);

            list.Add(new
            {
                studentId,
                fullName,
                email,
                addedAt
            });
        }
    }

    return Results.Ok(list);
});

app.MapGet("/classes/{classId}/cases", async (string classId, HttpContext ctx) =>
{
    // Ensure only instructors can view class cases
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1) Confirm the class belongs to this instructor
    using (var checkClass = conn.CreateCommand())
    {
        checkClass.CommandText = @"
            SELECT COUNT(*)
            FROM Classes
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkClass.Parameters.AddWithValue("$classId", classId);
        checkClass.Parameters.AddWithValue("$instructorId", me);

        var exists = (long)(await checkClass.ExecuteScalarAsync() ?? 0L);
        if (exists == 0)
        {
            return Results.NotFound(new { error = "Class not found or not owned by you" });
        }
    }

    // 2) Fetch assigned cases
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 
            cc.UploadId,
            cc.AssignedAt,
            u.OriginalFileName,
            u.Name
        FROM ClassCases cc
        JOIN Uploads u
            ON u.UploadId = cc.UploadId
        WHERE cc.ClassId = $classId
        ORDER BY cc.AssignedAt DESC;
    ";
    cmd.Parameters.AddWithValue("$classId", classId);

    var list = new List<object>();

    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var uploadId = reader.GetString(0);
            var assignedAt = reader.GetString(1);
            var originalName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var shortName = reader.IsDBNull(3) ? null : reader.GetString(3);

            list.Add(new
            {
                uploadId,
                fileName = string.IsNullOrWhiteSpace(shortName) ? originalName : shortName,
                assignedAt
            });
        }
    }

    return Results.Ok(list);
});

// INSTRUCTOR: view full message history of a student's session
app.MapGet("/classes/{classId}/sessions/{sessionId}", async (string classId, string sessionId, HttpContext ctx) =>
{
    // 1) Only instructors can call this
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var instructorId = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(instructorId))
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 2) Verify class belongs to instructor
    using (var checkCmd = conn.CreateCommand())
    {
        checkCmd.CommandText = @"
            SELECT COUNT(*)
            FROM Classes
            WHERE Id = $classId AND InstructorId = $instructorId;
        ";
        checkCmd.Parameters.AddWithValue("$classId", classId);
        checkCmd.Parameters.AddWithValue("$instructorId", instructorId);

        var exists = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
        if (exists == 0)
            return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    // 3) Load session, but ONLY if its user is enrolled in this class AND the case is assigned to this class
    string? userId = null;
    string? uploadId = null;
    string? createdAt = null;

    using (var sessionCmd = conn.CreateCommand())
    {
        sessionCmd.CommandText = @"
SELECT s.UserId, s.UploadId, s.CreatedAt
FROM Sessions s
JOIN ClassStudents cs
  ON cs.StudentId = s.UserId AND cs.ClassId = $classId
JOIN ClassCases cc
  ON cc.ClassId = cs.ClassId AND cc.UploadId = s.UploadId
WHERE s.Id = $sessionId;
";
        sessionCmd.Parameters.AddWithValue("$classId", classId);
        sessionCmd.Parameters.AddWithValue("$sessionId", sessionId);

        using var r = await sessionCmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return Results.NotFound(new { error = "Session not found for this class" });

        userId = r.GetString(0);
        uploadId = r.IsDBNull(1) ? null : r.GetString(1);
        createdAt = r.GetString(2);
    }

    // 4) Load student info
    string studentName = "";
    string studentEmail = "";

    using (var stuCmd = conn.CreateCommand())
    {
        stuCmd.CommandText = @"
SELECT FullName, Email
FROM Users
WHERE Id = $uid;
";
        stuCmd.Parameters.AddWithValue("$uid", userId);

        using var r = await stuCmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            studentName = r.IsDBNull(0) ? "" : r.GetString(0);
            studentEmail = r.IsDBNull(1) ? "" : r.GetString(1);
        }
    }

    // 5) Load upload/case filename
    string fileName = "";
    if (!string.IsNullOrWhiteSpace(uploadId))
    {
        using var fileCmd = conn.CreateCommand();
        fileCmd.CommandText = @"
SELECT OriginalFileName
FROM Uploads
WHERE UploadId = $up;
";
        fileCmd.Parameters.AddWithValue("$up", uploadId);

        using var r = await fileCmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            fileName = r.IsDBNull(0) ? "" : r.GetString(0);
        }
    }

    // 6) Load ALL messages
    var messages = new List<object>();
    using (var msgCmd = conn.CreateCommand())
    {
        msgCmd.CommandText = @"
SELECT Role, Content, CreatedAt
FROM Messages
WHERE SessionId = $sid
ORDER BY CreatedAt ASC;
";
        msgCmd.Parameters.AddWithValue("$sid", sessionId);

        using var r = await msgCmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            messages.Add(new
            {
                role = r.GetString(0),
                content = r.GetString(1),
                createdAt = r.GetString(2)
            });
        }
    }

    // 7) Final response
    return Results.Ok(new
    {
        sessionId,
        student = new
        {
            id = userId,
            fullName = studentName,
            email = studentEmail
        },
        caseInfo = new
        {
            uploadId,
            fileName
        },
        startedAt = createdAt,
        messages
    });
});






app.MapPost("/classes/{classId}/students", async (
    string classId,
    AddStudentRequest req,
    HttpContext ctx
) =>
{
    // Must be instructor (superuser)
    var instructorId = (string?)ctx.Items["userId"];
    var isSuper = (bool?)ctx.Items["isSuperUser"] ?? false;

    if (!isSuper)
        return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // 1 — Validate class exists AND belongs to instructor
    var checkCmd = conn.CreateCommand();
    checkCmd.CommandText = @"
        SELECT 1 FROM Classes
        WHERE Id = $cid AND InstructorId = $iid
        LIMIT 1";
    checkCmd.Parameters.AddWithValue("$cid", classId);
    checkCmd.Parameters.AddWithValue("$iid", instructorId);

    var exists = await checkCmd.ExecuteScalarAsync();
    if (exists is null)
        return Results.NotFound(new { error = "Class not found or not yours." });

    // 2 — Look up the student by email
    var findCmd = conn.CreateCommand();
    findCmd.CommandText = "SELECT Id FROM Users WHERE Email = $email LIMIT 1";
    findCmd.Parameters.AddWithValue("$email", req.Email);

    var studentIdObj = await findCmd.ExecuteScalarAsync();
    if (studentIdObj is null)
        return Results.NotFound(new { error = "No user with that email." });

    var studentId = (string)studentIdObj;

    // 3 — Insert into ClassStudents (ignore duplicate)
    var insertCmd = conn.CreateCommand();
    insertCmd.CommandText = @"
        INSERT OR IGNORE INTO ClassStudents (ClassId, StudentId, AddedAt)
        VALUES ($cid, $sid, $ts)";
    insertCmd.Parameters.AddWithValue("$cid", classId);
    insertCmd.Parameters.AddWithValue("$sid", studentId);
    insertCmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));

    await insertCmd.ExecuteNonQueryAsync();

    return Results.Ok(new { added = true, classId, studentId });
});


// GET /classes/mine  -> list classes owned by the logged-in instructor
app.MapGet("/classes/mine", async (HttpContext ctx) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 
            c.Id, 
            c.Name, 
            c.Description, 
            c.CreatedAt,
            (SELECT COUNT(*) FROM ClassStudents cs WHERE cs.ClassId = c.Id) AS StudentCount,
            (SELECT COUNT(*) FROM ClassCases cc WHERE cc.ClassId = c.Id)   AS CaseCount
        FROM Classes c
        WHERE c.InstructorId = $me
        ORDER BY c.CreatedAt DESC;
    ";
    cmd.Parameters.AddWithValue("$me", me);

    var list = new List<object>();

    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = reader.GetString(0);
        var name = reader.GetString(1);
        var description = reader.IsDBNull(2) ? null : reader.GetString(2);
        var createdAt = reader.GetString(3);
        var studentCount = Convert.ToInt32(reader.GetInt64(4));
        var caseCount = Convert.ToInt32(reader.GetInt64(5));

        list.Add(new { id, name, description, createdAt, studentCount, caseCount });
    }

    return Results.Ok(list);
});


app.MapGet("/classes/enrolled", async (HttpContext ctx) =>
{
    var me = ctx.Items["userId"] as string;
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var enrolledClasses = new List<object>();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // Get all classes where the student is enrolled
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT DISTINCT Classes.Id, Classes.Name, Classes.Description, Classes.InstructorId, Classes.CreatedAt
            FROM Classes
            JOIN ClassStudents ON Classes.Id = ClassStudents.ClassId
            WHERE ClassStudents.StudentId = $studentId
            ORDER BY Classes.CreatedAt DESC;
        ";
        cmd.Parameters.AddWithValue("$studentId", me);

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var classId = r.GetString(0);
            var name = r.GetString(1);
            var description = r.IsDBNull(2) ? null : r.GetString(2);
            var instructorId = r.GetString(3);
            var createdAt = r.GetString(4);

            // Get assigned cases for this class
            var cases = new List<object>();
            using (var caseCmd = conn.CreateCommand())
            {
                caseCmd.CommandText = @"
                    SELECT Uploads.UploadId, Uploads.OriginalFileName
                    FROM ClassCases
                    JOIN Uploads ON Uploads.UploadId = ClassCases.UploadId
                    WHERE ClassCases.ClassId = $classId;
                ";
                caseCmd.Parameters.AddWithValue("$classId", classId);

                using var caseReader = await caseCmd.ExecuteReaderAsync();
                while (await caseReader.ReadAsync())
                {
                    cases.Add(new
                    {
                        uploadId = caseReader.GetString(0),
                        fileName = caseReader.GetString(1)
                    });
                }
            }

            enrolledClasses.Add(new
            {
                classId,
                name,
                description,
                instructorId,
                createdAt,
                cases
            });
        }
    }

    return Results.Ok(enrolledClasses);
});



app.MapDelete("/classes/{classId}/students/{studentId}", async (HttpContext ctx, int classId, int studentId) =>
{
    // 1) Auth: must be signed in
    var userIdClaim = ctx.User.FindFirst("userId")?.Value
                      ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var role = ctx.User.FindFirst("role")?.Value
               ?? ctx.User.FindFirst(ClaimTypes.Role)?.Value
               ?? "";

    if (string.IsNullOrWhiteSpace(userIdClaim))
        return Results.Unauthorized();

    if (!string.Equals(role, "instructor", StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    // 2) DB connect
    var connStr =
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration["ConnectionStrings:DefaultConnection"];
    await using var db = new SqliteConnection(connStr);
    await db.OpenAsync();

    // 3) Ownership check: class must exist and be owned by current instructor
    await using (var checkCmd = db.CreateCommand())
    {
        checkCmd.CommandText = "SELECT InstructorId FROM Classes WHERE Id = $classId";
        checkCmd.Parameters.AddWithValue("$classId", classId);

        var instructorIdObj = await checkCmd.ExecuteScalarAsync();
        if (instructorIdObj is null)
            return Results.NotFound(new { error = "class not found" });

        var instructorIdStr = Convert.ToString(instructorIdObj) ?? "";
        if (!string.Equals(instructorIdStr, userIdClaim, StringComparison.Ordinal))
            return Results.Forbid();
    }

    // 4) Delete enrollment row
    await using (var delCmd = db.CreateCommand())
    {
        delCmd.CommandText = @"
            DELETE FROM ClassStudents
            WHERE ClassId = $classId AND StudentId = $studentId
        ";
        delCmd.Parameters.AddWithValue("$classId", classId);
        delCmd.Parameters.AddWithValue("$studentId", studentId);

        var affected = await delCmd.ExecuteNonQueryAsync();
        if (affected == 0)
            return Results.NotFound(new { error = "enrollment not found" });
    }

    return Results.NoContent(); 
})
.RequireAuthorization();




app.MapDelete("/classes/{classId}/cases/{uploadId}", async (HttpContext ctx, int classId, int uploadId) =>
{
    // 1) Auth required
    var userIdClaim = ctx.User.FindFirst("userId")?.Value
                      ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var role = ctx.User.FindFirst("role")?.Value
               ?? ctx.User.FindFirst(ClaimTypes.Role)?.Value
               ?? "";

    if (string.IsNullOrWhiteSpace(userIdClaim))
        return Results.Unauthorized();

    if (!string.Equals(role, "instructor", StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    // 2) DB connect
    var connStr =
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration["ConnectionStrings:DefaultConnection"];
    await using var db = new SqliteConnection(connStr);
    await db.OpenAsync();

    // 3) Must own the class
    await using (var checkCmd = db.CreateCommand())
    {
        checkCmd.CommandText = "SELECT InstructorId FROM Classes WHERE Id = $classId";
        checkCmd.Parameters.AddWithValue("$classId", classId);

        var instructorIdObj = await checkCmd.ExecuteScalarAsync();
        if (instructorIdObj is null)
            return Results.NotFound(new { error = "class not found" });

        var instructorIdStr = Convert.ToString(instructorIdObj) ?? "";
        if (!string.Equals(instructorIdStr, userIdClaim, StringComparison.Ordinal))
            return Results.Forbid();
    }

    // 4) Delete case assignment
    await using (var delCmd = db.CreateCommand())
    {
        delCmd.CommandText = @"
            DELETE FROM ClassCases
            WHERE ClassId = $classId AND UploadId = $uploadId
        ";
        delCmd.Parameters.AddWithValue("$classId", classId);
        delCmd.Parameters.AddWithValue("$uploadId", uploadId);

        var affected = await delCmd.ExecuteNonQueryAsync();
        if (affected == 0)
            return Results.NotFound(new { error = "assignment not found" });
    }

    return Results.NoContent(); // 204
})
.RequireAuthorization();


app.MapPatch("/sessions/{sessionId}/notes/{noteId:int}", async (
    HttpContext ctx,
    string sessionId,
    int noteId,
    NoteUpdateRequest body
) =>
{
    var me = (string?)ctx.Items["userId"];
    if (string.IsNullOrEmpty(me)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    // Ownership check
    using var check = conn.CreateCommand();
    check.CommandText = @"
SELECT 1
FROM Notes
WHERE Id = $id AND UserId = $me AND SessionId = $sid
LIMIT 1";
    check.Parameters.AddWithValue("$id", noteId);
    check.Parameters.AddWithValue("$me", me);
    check.Parameters.AddWithValue("$sid", sessionId);

    var exists = await check.ExecuteScalarAsync();
    if (exists is null)
        return Results.NotFound();

    // Update note
    using var update = conn.CreateCommand();
    update.CommandText = @"
UPDATE Notes
SET Text = $text
WHERE Id = $id";
    update.Parameters.AddWithValue("$text", body.Text.Trim());
    update.Parameters.AddWithValue("$id", noteId);

    await update.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        id = noteId,
        text = body.Text
    });
});

app.MapDelete("/sessions/{sessionId}/notes/{noteId:int}", async (
    HttpContext ctx,
    string sessionId,
    int noteId
) =>
{
    var me = (string?)ctx.Items["userId"];
    if (string.IsNullOrEmpty(me)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    using var del = conn.CreateCommand();
    del.CommandText = @"
DELETE FROM Notes
WHERE Id = $id AND UserId = $me AND SessionId = $sid
";
    del.Parameters.AddWithValue("$id", noteId);
    del.Parameters.AddWithValue("$me", me);
    del.Parameters.AddWithValue("$sid", sessionId);

    var affected = await del.ExecuteNonQueryAsync();
    if (affected == 0)
        return Results.NotFound(new { error = "note not found" });

    return Results.NoContent();
});


app.MapGet("/me", async (HttpContext ctx) =>
{
    var me = (string?)ctx.Items["userId"];
    if (string.IsNullOrEmpty(me)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
SELECT Id, Email, FullName
FROM Users
WHERE Id = $id";
    cmd.Parameters.AddWithValue("$id", me);

    using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound();

    return Results.Ok(new
    {
        userId = reader.GetString(0),
        email = reader.GetString(1),
        fullName = reader.IsDBNull(2) ? null : reader.GetString(2)
    });
});


app.MapPatch("/me", async (
    HttpContext ctx,
    UpdateProfileRequest body
) =>
{
    var me = (string?)ctx.Items["userId"];
    if (string.IsNullOrEmpty(me)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
UPDATE Users
SET FullName = $name
WHERE Id = $id";
    cmd.Parameters.AddWithValue("$name", body.FullName.Trim());
    cmd.Parameters.AddWithValue("$id", me);

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { fullName = body.FullName });
});




app.Run();


static async Task<QuestionType> ClassifyQuestionAsync(string question)
{
    // Very short or empty → treat as Other
    if (string.IsNullOrWhiteSpace(question))
        return QuestionType.Other;

    // IMPROVED: Do simple pattern matching FIRST before calling the model
    var q = question.ToLowerInvariant();
    
    // Strong methodology signals
    if (Regex.IsMatch(q, @"\b(method(s|ology)?|approach(es)?|procedure|technique|experimental (setup|design|approach)|how (did|were).*?(conduct|perform|collect|measure|analyze))\b"))
        return QuestionType.Method;
    
    // Strong findings signals
    if (Regex.IsMatch(q, @"\b(finding(s)?|result(s)?|outcome(s)?|what (did|were).*?(find|discover|observe|show|demonstrate))\b"))
        return QuestionType.Findings;
    
    // Strong summary signals
    if (Regex.IsMatch(q, @"\b(summary|summarize|overview|about|main (point|idea)|key (point|takeaway)|abstract)\b"))
        return QuestionType.Summary;
    
    // Strong fact signals
    if (Regex.IsMatch(q, @"\b(who|when|where|which|what (is|are|was|were))\b"))
        return QuestionType.Fact;
    
    // Strong explanation signals
    if (Regex.IsMatch(q, @"\b(why|how|explain|rationale|reason)\b"))
        return QuestionType.WhyExplain;

    // If patterns didn't match, fall back to model classification
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    var classifierModel = Environment.GetEnvironmentVariable("OPENAI_CLASSIFIER_MODEL")
        ?? "gpt-4o-mini"; // Use a better model

    var client = new OpenAI.Chat.ChatClient(classifierModel, apiKey);

    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        new OpenAI.Chat.SystemChatMessage(
            "Classify this question about a research document. " +
            "Return ONLY ONE word: SUMMARY, FACT, METHOD, FINDINGS, WHY_EXPLAIN, or OTHER. " +
            "Nothing else."
        ),
        new OpenAI.Chat.UserChatMessage($"Question: {question}")
    };

    var options = new ChatCompletionOptions { Temperature = 0f };
    var result = client.CompleteChat(messages, options).Value;

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


public record ClassCreateDto(string Name, string? Description);
public record AddStudentToClassDto(string StudentEmail);

public record AssignCaseToClassDto(string UploadId);


public record AddStudentRequest(string Email);
public record NoteUpdateRequest(string Text);

public record UpdateProfileRequest(string FullName);







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
    // Improved query understanding with more patterns
    public static bool IsListy(string q)
    {
        var s = q ?? string.Empty;

        // Common list verbs & phrasings
        if (Regex.IsMatch(s, @"\b(list|all|which|enumerate|show|show me|give|give me|name|return|extract|identify|find all|find every|every|provide|report|catalog|compile|what\s+are|what\s+were|how many|count)\b", RegexOptions.IgnoreCase))
            return true;

        // Numeric cues
        if (Regex.IsMatch(s, @"[%+]", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(s, @"\b(date|dates|range|ranges|deadline|deadlines)\b", RegexOptions.IgnoreCase)) return true;

        return false;
    }

    // Safe cosine similarity
    public static float SafeCosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0 || b.Length == 0)
            return 0f;
        return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(a, b);
    }

    // Tokenize to alnum lowercase
    private static string[] Tokens(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        return Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]{2,}")
                    .Select(m => m.Value)
                    .ToArray();
    }

    // Small, hand-tuned synonym expansion for academic-style questions
    private static HashSet<string> ExpandQueryTerms(HashSet<string> original)
    {
        // Start with the original tokens
        var expanded = new HashSet<string>(original);

        void AddGroup(string[] keys, string[] synonyms)
        {
            if (!keys.Any(k => original.Contains(k))) return;
            foreach (var s in synonyms)
                expanded.Add(s);
        }

        // limitations / weaknesses / drawbacks
        AddGroup(
            new[] { "limitations", "limitation" },
            new[] { "weakness", "weaknesses", "drawback", "drawbacks", "constraint", "constraints", "challenge", "challenges" }
        );

        // findings / results / effects
        AddGroup(
            new[] { "findings", "finding", "results", "result" },
            new[] { "outcome", "outcomes", "impact", "impacts", "effect", "effects" }
        );

        // methodology / methods / approach
        AddGroup(
            new[] { "methodology", "methods", "method" },
            new[] { "approach", "design", "experimental" }
        );

        // future work / improvements
        AddGroup(
            new[] { "future", "improvements", "improvement", "recommendations", "recommendation" },
            new[] { "extension", "extensions", "further", "ongoing" }
        );

        // external validity / generalization
        AddGroup(
            new[] { "external", "validity", "generalization", "generalizability" },
            new[] { "replication", "replications", "scaling", "scaleup", "scalability" }
        );

        return expanded;
    }


    // IMPROVED: More generous lexical scoring
    private static float LexicalScore(string preview, HashSet<string> qset)
    {
        if (string.IsNullOrEmpty(preview) || qset.Count == 0) return 0f;
        var p = preview.ToLowerInvariant();

        float s = 0f;
        int matchCount = 0;

        foreach (var t in qset)
        {
            if (p.Contains(t))
            {
                matchCount++;
                // Weight matches by term frequency
                int occurrences = Regex.Matches(p, Regex.Escape(t), RegexOptions.IgnoreCase).Count;
                s += 1f + (occurrences - 1) * 0.3f; // bonus for multiple occurrences
            }
        }

        // Bonus for high match ratio
        float matchRatio = (float)matchCount / qset.Count;
        if (matchRatio > 0.5f) s += 2f;
        if (matchRatio > 0.75f) s += 2f;

        // Context bonuses
        if (p.Contains("@")) s += 0.5f;
        if (Regex.IsMatch(p, @"\b\d{4}\b")) s += 0.25f;

        return s;
    }

    // IMPROVED: More generous boost
    private static float Boost(string preview, HashSet<string> qset)
    {
        var p = preview?.ToLowerInvariant() ?? "";
        float b = 0f;
        int matches = 0;

        foreach (var t in qset)
        {
            if (p.Contains(t))
            {
                matches++;
                b += 0.05f; // increased from 0.03f
            }
        }

        if (p.Contains("@")) b += 0.03f;

        // Extra boost for multiple term matches
        if (matches >= 3) b += 0.05f;

        return Math.Min(b, 0.20f); // increased cap from 0.10f
    }

    // IMPROVED: More generous keyword fallback
    public static List<TopChunk> KeywordFallback(List<IndexedChunk> list, string q, int k = 8)
    {
        var qset = ExpandQueryTerms(new HashSet<string>(Tokens(q)));
        if (qset.Count == 0) return new List<TopChunk>();

        // LOWERED threshold - accept any match
        return list
            .Select(x => new { x.Page, x.Preview, lex = LexicalScore(x.Preview, qset) })
            .Where(r => r.lex > 0) // accept ANY match
            .OrderByDescending(r => r.lex)
            .Take(k * 2) // get more candidates
            .Select(r => new TopChunk(r.Page, r.Preview, Math.Min(0.25f, r.lex / 10f))) // score based on lexical
            .ToList();
    }

    // IMPROVED: Main selection with better defaults
    public static List<TopChunk> SelectTop(
        List<IndexedChunk> list,
        ReadOnlySpan<float> qVec,
        string q,
        bool forStreaming)
    {
        bool listy = IsListy(q);
        var qset = ExpandQueryTerms(new HashSet<string>(Tokens(q)));

        // IMPROVED: More generous K values
        int K = listy ? 25 : (forStreaming ? 15 : 12); // increased from 20/10/10

        // ADJUSTED: Less aggressive weighting to favor embeddings
        const float alpha = 0.70f; // embedding weight (reduced from 0.85)
        const float beta = 0.30f;  // lexical weight (increased from 0.15)

        float[] qVecArr = qVec.ToArray();

        // 1) Score ALL candidates first
        var cands = list.Select(x =>
        {
            var cos = SafeCosine(qVecArr, x.Vec.Span);
            var lex = LexicalScore(x.Preview, qset);
            var boo = Boost(x.Preview, qset);
            var fin = alpha * cos + beta * lex + boo;
            return new Cand(x.Page, x.Preview, x.Vec, cos, lex, boo, fin);
        })
        .OrderByDescending(c => c.Final)
        .Take(Math.Max(K * 6, 20)) // increased oversample from K*4
        .ToList();

        // 2) ADJUSTED MMR - less diversity for better recall
        var picked = MMR(cands, K, lambda: 0.85f); // increased from 0.7f to favor relevance

        return picked.Select(c => new TopChunk(c.Page, c.Preview, c.Final)).ToList();
    }

    // Internal record
    private record Cand(int Page, string Preview, ReadOnlyMemory<float> Vec, float Cos, float Lex, float Boost, float Final);

    // IMPROVED MMR with better diversity balance
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
                float maxSim = 0f;
                foreach (var s in chosen)
                {
                    var sim = SafeCosine(c.Vec.Span, s.Vec.Span);
                    if (sim > maxSim) maxSim = sim;
                }

                // MMR score: balance relevance vs diversity
                float score = lambda * c.Final - (1 - lambda) * maxSim;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            if (best != null)
            {
                chosen.Add(best);
                remaining.Remove(best);
            }
            else
            {
                break; // safety
            }
        }

        return chosen;
    }
}


public static class ContextStitching
{
    public static List<TopChunk> ExpandWithNeighbors(
        List<IndexedChunk> all,
        List<TopChunk> picks,
        int sideNeighbors = 2,        // increased from 1
        int maxTotalNeighbors = 10)   // increased from 6
    {
        if (picks == null || picks.Count == 0) return picks ?? new List<TopChunk>();

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
                if (added >= maxTotalNeighbors) break;

                // Previous neighbor on same page
                if (idx - offset >= 0 && all[idx - offset].Page == p.Page)
                {
                    var prev = all[idx - offset];
                    var k = $"{prev.Page}\u0001{prev.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(prev.Page, prev.Preview, p.Score * 0.95f));
                        added++;
                    }
                }

                // Next neighbor on same page
                if (added < maxTotalNeighbors && idx + offset < all.Count && all[idx + offset].Page == p.Page)
                {
                    var next = all[idx + offset];
                    var k = $"{next.Page}\u0001{next.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(next.Page, next.Preview, p.Score * 0.95f));
                        added++;
                    }
                }
            }

            if (added >= maxTotalNeighbors) break;
        }

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
        if (Regex.IsMatch(s,
        @"\b(what\s+is\s+the\s+(document|paper|thesis)\s+title\b|" +
        @"give\s+me\s+the\s+(document|paper|thesis)\s+title\b|" +
        @"title\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.Title;
        }
        if (Regex.IsMatch(s,
                @"\b(what\s+is\s+the\s+abstract\b|" +
                @"give\s+me\s+the\s+abstract\b|" +
                @"abstract\s+of\s+this\s+(paper|document|thesis)\b|" +
                @"show\s+the\s+abstract\b)"))
        {
            return SectionIntent.Abstract;
        }
        // Only treat as an "authors" question if it's explicitly about listing / naming them
        if (Regex.IsMatch(s,
                @"\b(who\s+(are|is)\s+the\s+authors?\b|" +
                @"list\s+the\s+authors?\b|" +
                @"author\s+names?\b|" +
                @"authors?\s+of\s+this\s+(paper|document))",
                RegexOptions.IgnoreCase))
        {
            return SectionIntent.Authors;
        }

        if (Regex.IsMatch(s,
                @"\b(what\s+are\s+the\s+affiliations?\b|" +
                @"list\s+the\s+affiliations?\b|" +
                @"affiliations?\s+of\s+the\s+authors?\b|" +
                @"which\s+institutions?\s+are\s+the\s+authors?\s+from\b)"))
        {
            return SectionIntent.Affiliations;
        }
        if (Regex.IsMatch(s, @"\b(introduction|background)\b")) return SectionIntent.Introduction;
        if (Regex.IsMatch(s, @"\b(conclusion|conclusions)\b")) return SectionIntent.Conclusion;
        if (Regex.IsMatch(s,
               @"\b((list|show|give)\s+the\s+(references|bibliography|works\s+cited)\b|" +
               @"what\s+are\s+the\s+(references|bibliography|works\s+cited)\b|" +
               @"(references|bibliography|works\s+cited)\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.References;
        }
        if (Regex.IsMatch(s,
               @"\b(what\s+are\s+the\s+keywords?\b|" +
               @"list\s+the\s+keywords?\b|" +
               @"keywords?\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.Keywords;
        }

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

    // Heuristic finders for methods / results-like sections
    public static List<TopChunk> FindMethodLikeSections(List<IndexedChunk> list)
    {
        // Look for headings like "Methods", "Materials and Methods", "Methodology"
        var pattern = @"\b(methods?|materials and methods|methodology)\b";

        var hits = list
            .Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
            .GroupBy(x => x.Page)
            .Select(g => g.First())
            .OrderBy(x => x.Page)
            .Select(x => new TopChunk(x.Page, x.Preview, 0.6f))
            .ToList();

        return hits;
    }

    public static List<TopChunk> FindFindingsLikeSections(List<IndexedChunk> list)
    {
        // Look for sections like "Results", "Findings", "Discussion"
        var pattern = @"\b(results?|findings?|results and discussion|discussion)\b";

        var hits = list
            .Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
            .GroupBy(x => x.Page)
            .Select(g => g.First())
            .OrderBy(x => x.Page)
            .Select(x => new TopChunk(x.Page, x.Preview, 0.6f))
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

        // 4b) If the candidate still looks more like a paragraph/abstract than a title, discard it
        if (!string.IsNullOrWhiteSpace(pick))
        {
            // Drop pure "ABSTRACT" lines
            if (Regex.IsMatch(pick, @"^\s*abstract\s*:?\s*$", RegexOptions.IgnoreCase))
                return null;

            // Rough word-count limit: most real titles are not 30+ words
            var words = pick.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 30)
                return null;

            // Rough multi-sentence check – abstracts usually have several sentences
            var sentenceEndCount = Regex.Matches(pick, "[\\.\\?!]").Count;
            if (sentenceEndCount > 2)
                return null;
        }

        return pick;


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


public static class TextChunking
{
    // ... your existing ChunkBySize method stays here ...

    /// <summary>
    /// Improved chunking that respects sentence boundaries better than ChunkBySize
    /// Use this instead of ChunkBySize for better results
    /// </summary>
    public static IEnumerable<string> ChunkBySentences(string text, int maxChars = 1000, int overlap = 160)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        if (maxChars <= 0) yield break;
        if (overlap < 0) overlap = 0;

        // First, split into sentences properly
        var sentences = SplitIntoSentences(text);
        if (!sentences.Any()) yield break;

        var currentChunk = new System.Text.StringBuilder();
        var overlapText = "";

        foreach (var sentence in sentences)
        {
            // If adding this sentence would exceed max chars, yield current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + sentence.Length + 1 > maxChars)
            {
                var chunkText = currentChunk.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    yield return chunkText;

                    // Calculate overlap - take last N characters but try to start at a sentence
                    if (overlap > 0 && chunkText.Length > overlap / 2)
                    {
                        // Find a sentence boundary in the last part of the chunk for overlap
                        int overlapStart = Math.Max(0, chunkText.Length - overlap);

                        // Try to find a sentence start (capital letter after period)
                        for (int i = overlapStart; i < chunkText.Length - 1; i++)
                        {
                            if (chunkText[i] == '.' && i + 2 < chunkText.Length && char.IsUpper(chunkText[i + 2]))
                            {
                                overlapStart = i + 2;
                                break;
                            }
                        }

                        overlapText = chunkText.Substring(overlapStart);
                    }
                }

                // Start new chunk with overlap
                currentChunk.Clear();
                if (!string.IsNullOrWhiteSpace(overlapText))
                {
                    currentChunk.Append(overlapText);
                    currentChunk.Append(" ");
                }
            }

            // Add sentence to current chunk
            if (currentChunk.Length > 0) currentChunk.Append(" ");
            currentChunk.Append(sentence);
        }

        // Yield any remaining text
        var finalChunk = currentChunk.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(finalChunk) && finalChunk.Length > 50) // Avoid tiny chunks
        {
            yield return finalChunk;
        }
    }

    /// <summary>
    /// Helper method to split text into sentences intelligently
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var sentences = new List<string>();
        var currentSentence = new System.Text.StringBuilder();

        // Common abbreviations that don't end sentences
        var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dr", "Mr", "Mrs", "Ms", "Prof", "Ph.D", "M.D", "et al",
            "i.e", "e.g", "etc", "vs", "Fig", "Vol", "pp", "No"
        };

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            currentSentence.Append(c);

            // Check for sentence endings
            if (c == '.' || c == '!' || c == '?')
            {
                // Look ahead to see if this is really the end of a sentence
                bool isEnd = true;

                // Check if it's an abbreviation
                if (c == '.')
                {
                    // Get the word before the period
                    var beforePeriod = currentSentence.ToString().TrimEnd('.');
                    var lastWord = beforePeriod.Split(' ', '\n', '\t').LastOrDefault()?.Trim();

                    if (!string.IsNullOrEmpty(lastWord) && abbreviations.Contains(lastWord))
                    {
                        isEnd = false;
                    }

                    // Check for numbers (like 3.14)
                    if (i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
                    {
                        isEnd = false;
                    }

                    // Check if next character is lowercase (continuation)
                    if (i + 2 < text.Length && char.IsLower(text[i + 2]))
                    {
                        isEnd = false;
                    }
                }

                // If this is the end of a sentence and we have a space or newline next
                if (isEnd && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
                {
                    sentences.Add(currentSentence.ToString().Trim());
                    currentSentence.Clear();

                    // Skip whitespace
                    i++;
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    continue;
                }
            }

            i++;
        }

        // Add any remaining text as the last sentence
        if (currentSentence.Length > 0)
        {
            sentences.Add(currentSentence.ToString().Trim());
        }

        return sentences;
    }
}










