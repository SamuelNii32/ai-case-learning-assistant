using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Endpoints;
using Api.Extensions;
using Api.Infrastructure;
// iText7 for page count + raster image counting
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser; 
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenAI.Chat;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;











 












var builder = WebApplication.CreateBuilder(args);

if (args.Any(arg => string.Equals(arg, "--worker", StringComparison.OrdinalIgnoreCase)))
{
    await RunIndexWorkerOnlyAsync(args);
    return;
}

if (args.Any(arg => string.Equals(arg, "--migrate-sqlite-to-postgres", StringComparison.OrdinalIgnoreCase)))
{
    await DatabaseMigrator.MigrateSqliteToPostgresAsync(builder.Configuration);
    return;
}

var authSettings = AuthSettings.Load(builder.Configuration);
builder.Services.AddAppServices(builder.Configuration, authSettings);
// Read OpenAI config (API key + models)
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

// Answer model: big brain for actual answers (default gpt-5.1)
var answerModel = Environment.GetEnvironmentVariable("OPENAI_ANSWER_MODEL")
    ?? "gpt-5.1";

// Classifier model: cheap model for question type classification (default gpt-5-mini)
var classifierModel = Environment.GetEnvironmentVariable("OPENAI_CLASSIFIER_MODEL")
    ?? "gpt-5-mini";














var app = builder.Build();
app.UseAppPipeline();
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var databaseOptions = DatabaseOptions.Load(builder.Configuration);
var connString = databaseOptions.ConnectionString;

using (var conn = databaseOptions.CreateConnection())
{
    conn.Open();

    Console.WriteLine(databaseOptions.LocalPath is null
        ? $"[DB] Using configured {databaseOptions.Provider} connection string."
        : $"[DB PATH] Using ingestion.db at: {databaseOptions.LocalPath}");



    // (already inside: using var conn = new SqliteConnection(connString)); conn.Open();

    // 1) Create tables (SQL ONLY here)
    var cmd = conn.CreateCommand();
    cmd.CommandText = databaseOptions.Provider == "sqlite" ? @"
CREATE TABLE IF NOT EXISTS Users (
  Id TEXT PRIMARY KEY,
  Email TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  FullName TEXT NULL,
  IsSuperUser INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Sessions (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NULL,
  ClassId TEXT NULL,
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
  JoinCode TEXT NULL,
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
  Objective TEXT NULL,
  Focus TEXT NULL,
  DueAt TEXT NULL,
  ReadingCoachQuestions TEXT NULL,
  AssignedAt TEXT NOT NULL,
  PRIMARY KEY (ClassId, UploadId)
);

CREATE TABLE IF NOT EXISTS TutorSessions (
  SessionId TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  Category TEXT NOT NULL,
  Focus TEXT NULL,
  CurrentNode TEXT NOT NULL,
  VisitedTopicsJson TEXT NOT NULL,
  VisitedPagesJson TEXT NOT NULL,
  HistoryJson TEXT NOT NULL,
  LastStepSummary TEXT NULL,
  DrillPathJson TEXT NULL,
  PendingDrillChoicesJson TEXT NULL,
  CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TutorAnswers (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SessionId TEXT NOT NULL,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  StepId TEXT NOT NULL,
  Question TEXT NOT NULL,
  Answer TEXT NOT NULL,
  Feedback TEXT NOT NULL,
  Score REAL NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TutorHelpEvents (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  ChatSessionId TEXT NULL,
  TutorSessionId TEXT NULL,
  StepId TEXT NULL,
  Question TEXT NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS IndexJobs (
  UploadId TEXT PRIMARY KEY,
  Status TEXT NOT NULL,
  RequestedBy TEXT NULL,
  CreatedAt TEXT NOT NULL,
  StartedAt TEXT NULL,
  CompletedAt TEXT NULL,
  Attempts INTEGER NOT NULL DEFAULT 0,
  LastError TEXT NULL,
  ResultJson TEXT NULL,
  WorkerId TEXT NULL,
  UpdatedAt TEXT NOT NULL,
  LastHeartbeatAt TEXT NULL
);



" : @"
CREATE TABLE IF NOT EXISTS Users (
  Id TEXT PRIMARY KEY,
  Email TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  FullName TEXT NULL,
  IsSuperUser INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Sessions (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NULL,
  ClassId TEXT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Messages (
  Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  SessionId TEXT NOT NULL,
  Role TEXT NOT NULL,
  Content TEXT NOT NULL,
  Citations TEXT NULL,
  PagesUsed TEXT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Notes (
  Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
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
  JoinCode TEXT NULL,
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
  Objective TEXT NULL,
  Focus TEXT NULL,
  DueAt TEXT NULL,
  ReadingCoachQuestions TEXT NULL,
  AssignedAt TEXT NOT NULL,
  PRIMARY KEY (ClassId, UploadId)
);

CREATE TABLE IF NOT EXISTS TutorSessions (
  SessionId TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  Category TEXT NOT NULL,
  Focus TEXT NULL,
  CurrentNode TEXT NOT NULL,
  VisitedTopicsJson TEXT NOT NULL,
  VisitedPagesJson TEXT NOT NULL,
  HistoryJson TEXT NOT NULL,
  LastStepSummary TEXT NULL,
  DrillPathJson TEXT NULL,
  PendingDrillChoicesJson TEXT NULL,
  CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TutorAnswers (
  Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  SessionId TEXT NOT NULL,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  StepId TEXT NOT NULL,
  Question TEXT NOT NULL,
  Answer TEXT NOT NULL,
  Feedback TEXT NOT NULL,
  Score REAL NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TutorHelpEvents (
  Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  UserId TEXT NOT NULL,
  UploadId TEXT NOT NULL,
  ChatSessionId TEXT NULL,
  TutorSessionId TEXT NULL,
  StepId TEXT NULL,
  Question TEXT NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS IndexJobs (
  UploadId TEXT PRIMARY KEY,
  Status TEXT NOT NULL,
  RequestedBy TEXT NULL,
  CreatedAt TEXT NOT NULL,
  StartedAt TEXT NULL,
  CompletedAt TEXT NULL,
  Attempts INTEGER NOT NULL DEFAULT 0,
  LastError TEXT NULL,
  ResultJson TEXT NULL,
  WorkerId TEXT NULL,
  UpdatedAt TEXT NOT NULL,
  LastHeartbeatAt TEXT NULL
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
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists.
    }

    using (var indexes = conn.CreateCommand())
    {
        indexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_Sessions_UserId_CreatedAt ON Sessions (UserId, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Sessions_ClassId_CreatedAt ON Sessions (ClassId, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Sessions_UploadId ON Sessions (UploadId);
CREATE INDEX IF NOT EXISTS IX_Messages_SessionId_CreatedAt ON Messages (SessionId, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_Notes_SessionId ON Notes (SessionId);
CREATE INDEX IF NOT EXISTS IX_ClassStudents_ClassId_StudentId ON ClassStudents (ClassId, StudentId);
CREATE INDEX IF NOT EXISTS IX_ClassStudents_StudentId_ClassId ON ClassStudents (StudentId, ClassId);
CREATE INDEX IF NOT EXISTS IX_ClassCases_ClassId_UploadId ON ClassCases (ClassId, UploadId);
CREATE INDEX IF NOT EXISTS IX_TutorSessions_UserId_UploadId_Focus ON TutorSessions (UserId, UploadId, Focus);
CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email);
CREATE INDEX IF NOT EXISTS IX_IndexJobs_Status_CreatedAt ON IndexJobs (Status, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_IndexJobs_WorkerId ON IndexJobs (WorkerId);
";
        indexes.ExecuteNonQuery();
    }


    // 3) Add Name column to Uploads (safe if already exists)
    try
    {
        using var mig2 = conn.CreateCommand();
        mig2.CommandText = "ALTER TABLE Uploads ADD COLUMN Name TEXT NULL";
        mig2.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
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
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
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
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    try
    {
        using var mig5 = conn.CreateCommand();
        mig5.CommandText = "ALTER TABLE Classes ADD COLUMN JoinCode TEXT NULL";
        mig5.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    try
    {
        using var mig6 = conn.CreateCommand();
        mig6.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Classes_JoinCode ON Classes(JoinCode)";
        mig6.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Failed to create the unique class join-code index.", ex);
    }

    try
    {
        using var mig7 = conn.CreateCommand();
        mig7.CommandText = "ALTER TABLE ClassCases ADD COLUMN Objective TEXT NULL";
        mig7.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    try
    {
        using var mig8 = conn.CreateCommand();
        mig8.CommandText = "ALTER TABLE ClassCases ADD COLUMN Focus TEXT NULL";
        mig8.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    try
    {
        using var mig9 = conn.CreateCommand();
        mig9.CommandText = "ALTER TABLE ClassCases ADD COLUMN DueAt TEXT NULL";
        mig9.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    try
    {
        using var mig10 = conn.CreateCommand();
        mig10.CommandText = "ALTER TABLE ClassCases ADD COLUMN ReadingCoachQuestions TEXT NULL";
        mig10.ExecuteNonQuery();
    }
    catch (Exception ex) when (DatabaseSchemaErrors.IsDuplicateColumn(ex))
    {
        // Column already exists -> ignore
    }

    using (var indexes = conn.CreateCommand())
    {
        indexes.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_Uploads_UserId ON Uploads(UserId);
CREATE INDEX IF NOT EXISTS IX_Sessions_UserId ON Sessions(UserId);
CREATE INDEX IF NOT EXISTS IX_Sessions_UploadId ON Sessions(UploadId);
CREATE INDEX IF NOT EXISTS IX_Messages_SessionId ON Messages(SessionId);
CREATE INDEX IF NOT EXISTS IX_Notes_SessionId ON Notes(SessionId);
CREATE INDEX IF NOT EXISTS IX_Notes_UploadId ON Notes(UploadId);
CREATE INDEX IF NOT EXISTS IX_ClassStudents_StudentId ON ClassStudents(StudentId);
CREATE INDEX IF NOT EXISTS IX_ClassCases_UploadId ON ClassCases(UploadId);
CREATE INDEX IF NOT EXISTS IX_TutorSessions_UserUpload ON TutorSessions(UserId, UploadId);
";
        indexes.ExecuteNonQuery();
    }

    try
    {
        using var backfill = conn.CreateCommand();
        backfill.CommandText = @"
UPDATE Sessions
SET ClassId = (
    SELECT cs.ClassId
    FROM ClassStudents cs
    JOIN ClassCases cc ON cc.ClassId = cs.ClassId
    WHERE cs.StudentId = Sessions.UserId
      AND UPPER(cc.UploadId) = UPPER(Sessions.UploadId)
    ORDER BY cc.AssignedAt DESC
    LIMIT 1
)
WHERE ClassId IS NULL
  AND UploadId IS NOT NULL
  AND EXISTS (
      SELECT 1
      FROM ClassStudents cs
      JOIN ClassCases cc ON cc.ClassId = cs.ClassId
      WHERE cs.StudentId = Sessions.UserId
        AND UPPER(cc.UploadId) = UPPER(Sessions.UploadId)
  );";
        backfill.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[DB MIGRATION WARNING] Session ClassId backfill failed: {ex.Message}");
    }
}

if (args.Any(arg => string.Equals(arg, "--verify-database", StringComparison.OrdinalIgnoreCase)))
{
    using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await DatabaseVerification.RunAsync(app.Services, verificationTimeout.Token);
    Console.WriteLine("[DB VERIFY] Provider schema, repositories, and worker claims passed.");
    await app.DisposeAsync();
    return;
}

app.MapAuthEndpoints(authSettings.JwtSecret, authSettings.JwtIssuer, authSettings.JwtAudience);
app.MapDebugEndpoints(databaseOptions, app.Services.GetRequiredService<IUploadRepository>(), app.Services.GetRequiredService<ISessionRepository>());
app.MapUploadEndpoints(connString);
app.MapTutorEndpoints(databaseOptions);




bool IsInstructor(HttpContext ctx)
{
    return ctx.User.HasClaim("role", "instructor");
}

IResult? RequireInstructor(HttpContext ctx)
{
    if (!IsInstructor(ctx))
        return Results.Forbid();

    return null;
}

bool DebugEndpointsEnabled()
{
    return !app.Environment.IsProduction() ||
           string.Equals(
               Environment.GetEnvironmentVariable("ENABLE_DEBUG_ENDPOINTS"),
               "true",
               StringComparison.OrdinalIgnoreCase);
}

static async Task RunIndexWorkerOnlyAsync(string[] workerArgs)
{
    Environment.SetEnvironmentVariable("RUN_BACKGROUND_WORKER", "true");
    var workerBuilder = Host.CreateApplicationBuilder(workerArgs);
    var authSettings = AuthSettings.Load(workerBuilder.Configuration);
    workerBuilder.Services.AddAppServices(workerBuilder.Configuration, authSettings);

    using var workerHost = workerBuilder.Build();
    await workerHost.RunAsync();
}

async Task<bool> CanAccessUploadAsync(Guid uploadId, string userId)
{
    using var conn = databaseOptions.CreateConnection();
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER(@uploadId)
  AND (
        u.UserId = @userId
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = @userId
        )
  )
LIMIT 1;
";
    cmd.AddWithValue("@uploadId", uploadId.ToString());
    cmd.AddWithValue("@userId", userId);

    return await cmd.ExecuteScalarAsync() is not null;
}





















if (!app.Environment.IsProduction())
{
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
            // client disconnected; ignore
        }
    });
}

// Figures/visuals for a document (MVP: backed by layout manifest)
// GET /api/documents/{caseId}/figures
app.MapGet("/api/documents/{caseId}/figures", async (string caseId, HttpContext ctx, IWebHostEnvironment env) =>
{
    if (!Guid.TryParse(caseId, out var uploadId))
    {
        return Results.BadRequest(new { error = "invalid upload id" });
    }

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await CanAccessUploadAsync(uploadId, me)) return Results.NotFound(new { error = "not found" });

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var layoutPath = Path.Combine(uploadsRoot, $"{uploadId}.layout.json");
    if (!File.Exists(layoutPath))
    {
        var pdfPath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");
        if (!File.Exists(pdfPath)) return Results.NotFound(new { error = "PDF not found" });
        await DocumentLayoutAnalyzer.AnalyzeAndSaveAsync(uploadId, env);
    }

    var json = await File.ReadAllTextAsync(layoutPath);
    var manifest = System.Text.Json.JsonSerializer.Deserialize<LayoutManifest>(json);
    var captionedEvidence = (manifest?.Captions ?? new List<LayoutCaption>())
        .Select(c => new
        {
            id = c.Id,
            page = c.Page,
            type = c.Kind,
            label = c.Label,
            caption = c.Text,
            bbox = c.BBox,
            confidence = c.Confidence,
            reasons = c.Reasons,
            source = "caption"
        });

    var tableCandidates = (manifest?.Tables ?? new List<LayoutTableCandidate>())
        .Select(t => new
        {
            id = t.Id,
            page = t.Page,
            type = "table",
            label = t.Label,
            caption = t.TextPreview,
            bbox = t.BBox,
            confidence = t.Confidence,
            reasons = t.Reasons,
            source = "candidate"
        });

    return Results.Json(captionedEvidence.Concat(tableCandidates).OrderBy(x => x.page).ThenBy(x => x.label));
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
app.MapGet("/uploads/{uploadId:guid}/pages/preview", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await CanAccessUploadAsync(uploadId, me)) return Results.NotFound();

    var pdfPath = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
    if (!System.IO.File.Exists(pdfPath)) return Results.NotFound();

    var preview = ExtractPerPageText(pdfPath)
        .Take(3)
        .Select(p => new
        {
            page = p.page,
            snippet = TextUtilityHelpers.SafeHead(p.text, 300) + (p.text.Length > 300 ? "…" : "")
        });

    return Results.Json(preview);
});


// ---- simple in-memory vector index ----

app.MapPost("/index/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, IndexJobStore jobStore, IndexingService indexingService) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    if (!await CanAccessUploadAsync(uploadId, me))
        return Results.NotFound(new { error = "not found" }); // don't leak existence

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var pdfPath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");
    if (!System.IO.File.Exists(pdfPath))
        return Results.NotFound(new { error = "PDF not found" });

    var existingJob = await jobStore.GetAsync(uploadId, ctx.RequestAborted);
    if (existingJob?.Status is "queued" or "running")
    {
        var jobSummary = new
        {
            uploadId = existingJob.UploadId,
            status = existingJob.Status,
            requestedBy = existingJob.RequestedBy,
            createdAt = existingJob.CreatedAt,
            startedAt = existingJob.StartedAt,
            completedAt = existingJob.CompletedAt,
            attempts = existingJob.Attempts,
            lastError = existingJob.LastError,
            workerId = existingJob.WorkerId,
            updatedAt = existingJob.UpdatedAt,
            lastHeartbeatAt = existingJob.LastHeartbeatAt
        };
        return Results.Accepted($"/index/status/{uploadId}", new
        {
            uploadId,
            state = existingJob.Status,
            job = jobSummary
        });
    }

    if (existingJob?.Status == "completed" && !string.IsNullOrWhiteSpace(existingJob.ResultJson))
    {
        try
        {
            var completedSummary = System.Text.Json.JsonSerializer.Deserialize<IndexBuildSummary>(existingJob.ResultJson);
            if (completedSummary is not null)
            {
                return Results.Ok(completedSummary);
            }
        }
        catch
        {
            // fall through to cached rebuild path
        }
    }

    if (IndexPersistence.TryLoad(uploadId, env, out _))
    {
        var summary = await indexingService.BuildAsync(uploadId, ctx.RequestAborted);
        await jobStore.MarkCompletedAsync(uploadId, summary, cancellationToken: ctx.RequestAborted);
        return Results.Ok(summary);
    }

    var enqueued = await jobStore.EnqueueAsync(uploadId, me, ctx.RequestAborted);
    var enqueuedJob = new
    {
        uploadId = enqueued.UploadId,
        status = enqueued.Status,
        requestedBy = enqueued.RequestedBy,
        createdAt = enqueued.CreatedAt,
        startedAt = enqueued.StartedAt,
        completedAt = enqueued.CompletedAt,
        attempts = enqueued.Attempts,
        lastError = enqueued.LastError,
        workerId = enqueued.WorkerId,
        updatedAt = enqueued.UpdatedAt,
        lastHeartbeatAt = enqueued.LastHeartbeatAt
    };
    return Results.Accepted($"/index/status/{uploadId}", new
    {
        uploadId,
        state = enqueued.Status,
        job = enqueuedJob
    });
});

// GET /uploads/{uploadId}/classification -> returns doc type & confidence
app.MapGet("/uploads/{uploadId:guid}/classification", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await CanAccessUploadAsync(uploadId, me)) return Results.NotFound(new { error = "No classification stored for this uploadId." });

    if (DocTypePersistence.TryLoad(uploadId, env, out var cls) && cls != null)
        return Results.Json(cls);

    return Results.NotFound(new { error = "No classification stored for this uploadId." });
});




// GET /search/{uploadId}?q=...  -> top-k chunks by cosine similarity
app.MapGet("/search/{uploadId:guid}", async (Guid uploadId, string q, HttpContext ctx, IWebHostEnvironment env) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await CanAccessUploadAsync(uploadId, me)) return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });

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
}).RequireRateLimiting("Ai");

app.MapGet("/debug/student-access/{uploadId}", async (Guid uploadId, HttpContext ctx) =>
{
    if (!DebugEndpointsEnabled()) return Results.NotFound();

    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId() ?? "";
    using var conn = databaseOptions.CreateConnection();
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
WHERE u.UploadId = @u;
";
    cmd.AddWithValue("@u", uploadId.ToString());

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
app.MapGet("/ask/{uploadId:guid}", async (Guid uploadId, string q, string? sessionId, HttpContext ctx, IWebHostEnvironment env, IMessageRepository messages) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    Console.WriteLine($"[ASK DEBUG] me={me}, uploadId={uploadId}");

    using (var conn = databaseOptions.CreateConnection())
    {
        await conn.OpenAsync();
        using var chk = conn.CreateCommand();
        chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE upper(u.UploadId) = upper(@u)
  AND (
        u.UserId = @me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE cc.UploadId = u.UploadId
              AND cs.StudentId = @me
        )
  )
LIMIT 1;
";


        chk.AddWithValue("@u", uploadId.ToString());
        chk.AddWithValue("@me", me);
        var ok = await chk.ExecuteScalarAsync();

        if (ok is null)
            return Results.NotFound(new { error = "not found" });

    }

    // Keep the original question and classify it with the small model
    var questionOriginal = q ?? string.Empty;

    // High-level classification: Summary / Fact / Method / Findings / WhyExplain / Other
    var questionType = await QuestionClassifier.ClassifyQuestionAsync(questionOriginal);


    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
    {
        if (!IndexPersistence.TryLoad(uploadId, env, out list))
            return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });
    }
    try
    {
        // --- record USER message (if a session was provided) ---
        await messages.SaveAsync(sessionId, "user", q ?? "", null, null, ctx.RequestAborted);

        // --- Q/A CACHE FAST PATH ---
        // If we've seen this exact question for this upload before,
        // reuse the previous answer instead of redoing retrieval + LLM.

        if (!string.IsNullOrWhiteSpace(q))
        {
            try
            {
                var cached = await messages.FindCachedAnswerAsync(uploadId, q, cancellationToken: ctx.RequestAborted);
                if (cached is not null)
                {
                    // Still write this assistant message into the current session history
                    await messages.SaveAsync(sessionId, "assistant", cached.Content, cached.Citations, cached.PagesUsed, ctx.RequestAborted);

                    return Results.Json(new
                    {
                        answer = cached.Content,
                        citations = cached.Citations ?? Array.Empty<int>(),
                        pagesUsed = cached.PagesUsed ?? Array.Empty<int>(),
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

        // map “title of this/the document/pdf” ? “document title”
        qNorm = Regex.Replace(qNorm, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                              "document title", RegexOptions.IgnoreCase);
        // map author-like phrasings ? "authors"
        qNorm = Regex.Replace(qNorm, @"\b(authors?|students?|contributors?|prepared\s+by|by\s+whom)\b",
                              "authors", RegexOptions.IgnoreCase);
        // map “findings/takeaways/insights/conclude” ? conclusion
        qNorm = Regex.Replace(qNorm, @"\b(key\s+findings?|findings?|key\s+takeaways?|takeaways?|insights?|what\s+did\s+they\s+conclude|conclusions?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “results/outcomes/observations/measurements” ? conclusion (closest existing intent)
        qNorm = Regex.Replace(
    qNorm,
    @"\b(results?|experimental\s+results?|outcomes?|observations?|measurements?|future\s+work|recommendations?|improvements?)\b",
    "conclusion",
    RegexOptions.IgnoreCase
);

        // map “summary/overview/tldr/summarize/in N bullets” ? abstract
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
                    : QueryWordHelpers.WordToInt(m.Groups["word"].Value);
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
                return await AnswerWithContext(ctxOnlyFront, qNorm, pagesOnlyFront, apiKey, catHint);
            }
            // if nothing found, fall through to normal flow
        }

        // ---- Embed the normalized query
        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
        var qVec = embClient.GenerateEmbedding(qNorm).Value.ToFloats();

        // ---- Retrieval
        var top = QaRetrieval.SelectTop(list, qVec.Span, qNorm, forStreaming: false);

        // ?? Phase 3: boost method / findings pages into the context
        var sectionHints = new List<TopChunk>();

        // If question is about methods/data collection ? pull method-like sections
        if (questionType == QuestionType.Method)
        {
            sectionHints = SectionSwitchboard.FindMethodLikeSections(list);
        }
        // If question is about findings / conclusions / "why" ? pull results/discussion
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
                return await AnswerWithContext(ctxSec, askQ, stitchedSec.Select(t => t.Page).Distinct().ToArray(), apiKey, catHint);
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
                    await messages.SaveAsync(sessionId, "assistant", answerText, null, null, ctx.RequestAborted);
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
                        await messages.SaveAsync(sessionId, "assistant", guess, null, pagesGuess, ctx.RequestAborted);
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
                await messages.SaveAsync(sessionId, "assistant", answerText, null, null, ctx.RequestAborted);

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
        return await AnswerWithContext(ctxStr, askQ, stitchedTop.Select(t => t.Page).Distinct().ToArray(), apiKey, catHint);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ASK ERROR] {ex.GetType().Name}: {ex.Message}");
        return Results.Json(new { error = ex.GetType().Name, message = ex.Message });
    }

    async Task<IResult> AnswerWithContext(string ctxStr, string question, int[] pages, string apiKeyLocal, string categoryHint)
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




        var chatMessages = new List<OpenAI.Chat.ChatMessage>
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

        var result = chat.CompleteChat(chatMessages, options).Value;

        var answer = string.Concat(result.Content.Select(part => part.Text ?? string.Empty)).Trim();

        if (string.IsNullOrWhiteSpace(answer))
        {
            var answerText =
                "I can't find that in the document. " +
                "The model could not extract a grounded answer from the provided context.";
            await messages.SaveAsync(sessionId, "assistant", answerText, null, pages, ctx.RequestAborted);

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

        await messages.SaveAsync(sessionId, "assistant", answer, citations, pages, ctx.RequestAborted);

        return Results.Json(new { answer, pagesUsed = pages, citations });
    }
}).RequireRateLimiting("Ai");


// GET /ask/stream/{uploadId}?q=...  -> SSE: token-by-token answer + citations + done
app.MapGet("/ask/stream/{uploadId}", async (
    string uploadId,
    string q,
    string? sessionId,
    string? tutorSessionId,
    string? tutorStepId,
    HttpContext ctx,
    IWebHostEnvironment env,
    IMessageRepository messages,
    IUploadRepository uploadsRepository,
    ITutorRepository tutorRepository) =>
{
    if (!Guid.TryParse(uploadId, out var parsedUploadId))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = "not found" });
        return;
    }


    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }

    if (!await uploadsRepository.CanAccessAsync(parsedUploadId, me, ctx.RequestAborted))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound; // don’t leak existence
        await ctx.Response.WriteAsJsonAsync(new { error = "not found" });
        return; // IMPORTANT in SSE handlers
    }
    // Keep the original question and classify it with the small model
    var questionOriginal = q ?? string.Empty;
    var hasTutorChatContext = !string.IsNullOrWhiteSpace(tutorSessionId) || !string.IsNullOrWhiteSpace(tutorStepId);
    var tutorChatContext = hasTutorChatContext
        ? await TutorChatContext.BuildAsync(databaseOptions, tutorSessionId, tutorStepId)
        : "";

    // High-level classification: Summary / Fact / Method / Findings / WhyExplain / Other
    var questionType = await QuestionClassifier.ClassifyQuestionAsync(questionOriginal);

    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"] = "keep-alive";
    await ctx.Response.WriteAsync("\n");
    await ctx.Response.Body.FlushAsync();

    if (!InMemoryStore.VectorIndex.TryGetValue(parsedUploadId.ToString(), out var list) || list.Count == 0)
    {
        if (!IndexPersistence.TryLoad(parsedUploadId, env, out list))
        {
            await ctx.Response.WriteAsync("event: error\ndata: {\"message\":\"Not indexed. POST /index first.\"}\n\n");
            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
            return;
        }
    }

    try
    {
        var recentConversationContext = await messages.LoadRecentConversationContextAsync(sessionId, ctx.RequestAborted);

        // --- record USER message at the start of the main happy path ---
        await messages.SaveAsync(sessionId, "user", q ?? "", null, null, ctx.RequestAborted);
        if (hasTutorChatContext && !string.IsNullOrWhiteSpace(me))
        {
            await tutorRepository.SaveHelpEventAsync(
                me,
                parsedUploadId,
                sessionId,
                tutorSessionId,
                tutorStepId,
                CleanTrackedQuestion(q),
                ctx.RequestAborted);
        }

        if (!hasTutorChatContext && !string.IsNullOrWhiteSpace(q))
        {
            var cached = await messages.FindCachedAnswerAsync(parsedUploadId, q, me, ctx.RequestAborted);
            if (cached is not null)
            {
                // Stream the cached answer as SSE:
                // 1) one token event with the full text
                await ctx.Response.WriteAsync(
                    $"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = cached.Content })}\n\n"
                );

                // 2) citations event (pages used)
                await ctx.Response.WriteAsync(
                    $"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(cached.PagesUsed)}\n\n"
                );

                // 3) persist this assistant message into history for this session too
                await messages.SaveAsync(sessionId, "assistant", cached.Content, cached.PagesUsed, cached.PagesUsed, ctx.RequestAborted);

                // 4) done
                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                await ctx.Response.Body.FlushAsync();
                return;
            }
        }

        // Normalize + shims
        var qNorm = QueryNormalization.Normalize(q ?? "");
        // map “title of this/the document/pdf” ? “document title”
        qNorm = Regex.Replace(qNorm, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                              "document title", RegexOptions.IgnoreCase);
        // map author-like phrasings ? "authors"
        qNorm = Regex.Replace(qNorm, @"\b(authors?|students?|contributors?|prepared\s+by|by\s+whom)\b",
                              "authors", RegexOptions.IgnoreCase);
        // map “findings/takeaways/insights/conclude” ? conclusion
        qNorm = Regex.Replace(qNorm, @"\b(key\s+findings?|findings?|key\s+takeaways?|takeaways?|insights?|what\s+did\s+they\s+conclude|conclusions?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “results/outcomes/observations/measurements” ? conclusion
        qNorm = Regex.Replace(qNorm, @"\b(results?|experimental\s+results?|outcomes?|observations?|measurements?)\b",
                              "conclusion", RegexOptions.IgnoreCase);
        // map “summary/overview/tldr/summarize/in N bullets” ? abstract
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
                await messages.SaveAsync(sessionId, "assistant", answerText, null, null, ctx.RequestAborted);

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
                await messages.SaveAsync(sessionId, "assistant", answerText, new[] { 1 }, new[] { 1 }, ctx.RequestAborted);

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
            if (!string.IsNullOrWhiteSpace(metaAuthor) &&
    !Regex.IsMatch(metaAuthor, @"^\s*(unknown|n/?a|none)\s*$", RegexOptions.IgnoreCase))
            {
                await ctx.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = metaAuthor })}\n\n");
                await ctx.Response.WriteAsync("event: citations\ndata: []\n\n");

                await messages.SaveAsync(sessionId, "assistant", metaAuthor, null, null, ctx.RequestAborted);

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
            await messages.SaveAsync(sessionId, "assistant", answerText, finalCites, finalCites, ctx.RequestAborted);

            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
            await ctx.Response.Body.FlushAsync();
            return;
        }

        // ==== normal retrieval (streamed) ====
        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
        var qVec = embClient.GenerateEmbedding(qNorm).Value.ToFloats();

        var top = QaRetrieval.SelectTop(list, qVec.Span, qNorm, forStreaming: true);

        // ?? Phase 3: boost method / findings pages into the context
        var sectionHints = new List<TopChunk>();

        // If question is about methods/data collection ? pull method-like sections
        if (questionType == QuestionType.Method)
        {
            sectionHints = SectionSwitchboard.FindMethodLikeSections(list);
        }
        // If question is about findings / conclusions / "why" ? pull results/discussion
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
        var contextPages = new List<int>();
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
                contextPages = stitchedSec.Select(t => t.Page).Distinct().OrderBy(p => p).ToList();
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

            // If retrieval is empty or below threshold ? deterministic fallbacks
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
                    await messages.SaveAsync(sessionId, "assistant", answerText, Array.Empty<int>(), Array.Empty<int>(), ctx.RequestAborted);

                    await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
                    await ctx.Response.Body.FlushAsync();
                    return;
                }

                var stitchedFb = ContextStitching.ExpandWithNeighbors(list, fb,
                    sideNeighbors: techGroup ? 2 : 1,
                    maxTotalNeighbors: techGroup ? 10 : 6);
                contextPages = stitchedFb.Select(t => t.Page).Distinct().OrderBy(p => p).ToList();
                context = string.Join("\n\n", stitchedFb.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
            }
            else
            {
                // Normal stitched context from 'top'
                var stitchedTop = ContextStitching.ExpandWithNeighbors(list, top,
                    sideNeighbors: techGroup ? 2 : 1,
                    maxTotalNeighbors: techGroup ? 10 : 6);
                contextPages = stitchedTop.Select(t => t.Page).Distinct().OrderBy(p => p).ToList();
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
If Tutor context is present, do not write a ready-made response to the
student's checkpoint question. Explain the confusing concept, then end with
2-3 ingredients the student can use in their own words.
{(string.IsNullOrWhiteSpace(summaryHint) ? "" : summaryHint + "\n")}
{(string.IsNullOrWhiteSpace(catHint) ? "" : catHint + "\n")}
{(string.IsNullOrWhiteSpace(tutorChatContext) ? "" : tutorChatContext + "\n")}
{(string.IsNullOrWhiteSpace(recentConversationContext) ? "" : recentConversationContext + "\n")}

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
        var allowedPages = contextPages.Count > 0
            ? new HashSet<int>(contextPages)
            : new HashSet<int>(top.Select(t => t.Page));
        var filteredPages2 = pages2.Where(p => allowedPages.Contains(p)).ToArray();
        if (filteredPages2.Length == 0 &&
            !Regex.IsMatch(answer2, @"I can't find that in the document", RegexOptions.IgnoreCase))
        {
            filteredPages2 = allowedPages.OrderBy(p => p).Take(3).ToArray();
        }

        await ctx.Response.WriteAsync($"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(filteredPages2)}\n\n");

        await messages.SaveAsync(sessionId, "assistant", answer2, filteredPages2, filteredPages2, ctx.RequestAborted);

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
}).RequireRateLimiting("Ai");





app.MapGet("/index/status/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, IndexJobStore jobStore) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await CanAccessUploadAsync(uploadId, me)) return Results.NotFound(new { error = "not found" });

    var id = uploadId.ToString();
    var inMemory = InMemoryStore.VectorIndex.TryGetValue(id, out var list) && list?.Count > 0;

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
    var onDisk = System.IO.File.Exists(indexPath);

    int? chunks = null;
    int? pagesIndexed = null;
    object? sample = null;
    string state = "missing";
    string? error = null;
    object? result = null;
    IndexJobRecord? job = await jobStore.GetAsync(uploadId, ctx.RequestAborted);

    if (job is not null)
    {
        state = job.Status;
        error = job.LastError;
    }

    if (onDisk)
    {
        try
        {
            var json = System.IO.File.ReadAllText(indexPath);
            var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
            chunks = rows?.Length;
            pagesIndexed = rows?.Select(x => x.Page).Distinct().Count();
            sample = rows?.Take(3).Select(x => new { page = x.Page, preview = x.Preview });
            state = state == "missing" ? "completed" : state;
        }
        catch { /* ignore */ }
    }
    else if (inMemory)
    {
        chunks = list!.Count;
        pagesIndexed = list.Select(x => x.Page).Distinct().Count();
        sample = list.Take(3).Select(x => new { page = x.Page, preview = x.Preview });
        state = state == "missing" ? "completed" : state;
    }

    if (job?.Status is "completed" && job.ResultJson is not null)
    {
        try
        {
            var summary = System.Text.Json.JsonSerializer.Deserialize<IndexBuildSummary>(job.ResultJson);
            if (summary is not null)
            {
                chunks = summary.Chunks;
                pagesIndexed = summary.PagesIndexed;
                sample = summary.Sample.Select(x => new { page = x.Page, preview = x.Preview });
                state = "completed";
                result = summary;
            }
        }
        catch { /* ignore */ }
    }

    var jobSummary = job is null ? null : new
    {
        uploadId = job.UploadId,
        status = job.Status,
        requestedBy = job.RequestedBy,
        createdAt = job.CreatedAt,
        startedAt = job.StartedAt,
        completedAt = job.CompletedAt,
        attempts = job.Attempts,
        lastError = job.LastError,
        workerId = job.WorkerId,
        updatedAt = job.UpdatedAt,
        lastHeartbeatAt = job.LastHeartbeatAt
    };

    return Results.Json(new
    {
        uploadId = id,
        state,
        inMemory,
        onDisk,
        chunks,
        pagesIndexed,
        sample,
        error,
        result,
        job = jobSummary
    });
});





app.MapGet("/uploads/mine", async (HttpContext ctx, IUploadRepository uploadsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    var query = ctx.Request.Query;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], Pagination.DefaultPageSize);
    var rows = await uploadsRepository.ListMineAsync(me, ctx.RequestAborted);
    var items = rows.Select(r => new
    {
        uploadId = r.UploadId,
        name = r.Name ?? "",
        originalFileName = r.OriginalFileName ?? "",
        createdAt = r.CreatedAt
    }).ToList();

    var skip = Pagination.Offset(page, pageSize);
    var pageItems = items.Skip(skip).Take(pageSize).ToList();
    return Results.Ok(new PagedResult<object>(pageItems.Cast<object>().ToList(), page, pageSize, items.Count));
});


// GET /sessions/mine -> list sessions for current user (with stats + lastMessagePreview)
app.MapGet("/sessions/mine", async (HttpContext ctx, ISessionRepository sessionsRepository, IDocumentStorage storage) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    var query = ctx.Request.Query;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], Pagination.DefaultPageSize);
    var search = query.TryGetValue("q", out var qValue) ? qValue.ToString() : null;
    var uploadId = query.TryGetValue("uploadId", out var uploadValue) ? uploadValue.ToString() : null;

    var rows = await sessionsRepository.ListMineAsync(me, page, pageSize, search, uploadId, ctx.RequestAborted);
    var sessions = new List<SessionMineDto>();
    foreach (var row in rows.Items)
    {
        var caseName = row.CaseName;

        if (!string.IsNullOrWhiteSpace(row.UploadId) && Guid.TryParse(row.UploadId, out var uploadGuid))
        {
            var summaryJson = await storage.ReadTextAsync(uploadGuid, ".summary.json", ctx.RequestAborted);
            if (!string.IsNullOrWhiteSpace(summaryJson))
            {
                try
                {
                    using var summaryDoc = JsonDocument.Parse(summaryJson);
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

        sessions.Add(new SessionMineDto
        {
            SessionId = row.SessionId,
            UploadId = row.UploadId,
            CaseName = caseName,
            CreatedAt = row.CreatedAt,
            LastActivityAt = row.LastActivityAt,
            DurationSec = row.DurationSec,
            MessageCount = row.MessageCount,
            NotesCount = row.NotesCount,
            LastMessagePreview = row.LastMessagePreview
        });
    }

    foreach (var group in sessions
        .Where(s => !string.IsNullOrWhiteSpace(s.UploadId))
        .GroupBy(s => (s.CaseName ?? "Untitled case").Trim(), StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1))
    {
        var ordered = group
            .OrderBy(s => DateTimeOffset.TryParse(s.CreatedAt, out var parsed) ? parsed : DateTimeOffset.MinValue)
            .ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            ordered[i].CaseName = $"{ordered[i].CaseName} ({i + 1})";
        }
    }

    return Results.Ok(new PagedResult<SessionMineDto>(sessions, rows.Page, rows.PageSize, rows.TotalCount));
});


// POST /sessions  -> create a chat thread (optionally tied to an upload)
app.MapPost("/sessions", async (HttpContext ctx, ISessionRepository sessionsRepository, IUploadRepository uploadsRepository) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

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

    if (!string.IsNullOrWhiteSpace(uploadId) &&
        (!Guid.TryParse(uploadId, out var parsedUploadId) || !await uploadsRepository.CanAccessAsync(parsedUploadId, me, ctx.RequestAborted)))
    {
        return Results.NotFound(new { error = "not found" });
    }

    var sessionId = Guid.NewGuid().ToString("N");
    await sessionsRepository.CreateAsync(sessionId, me, uploadId, DateTime.UtcNow, null, ctx.RequestAborted);

    return Results.Json(new { sessionId });
});

// GET /sessions/{id} -> full message history for a single session
app.MapGet("/sessions/{id}", async (string id, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";

    var rows = await sessionsRepository.GetOwnedMessagesAsync(id, me, ctx.RequestAborted);
    if (rows is null)
    {
        return Results.NotFound(new { error = "not found" });
    }

    return Results.Json(rows.Select(m => new
    {
        role = m.Role,
        content = m.Content,
        citations = m.Citations,
        pagesUsed = m.PagesUsed,
        createdAt = m.CreatedAt
    }));
});


// GET /sessions/{id}/notes -> list notes for a session (current user only)
app.MapGet("/sessions/{id}/notes", async (string id, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";

    var notes = await sessionsRepository.ListNotesAsync(id, me, ctx.RequestAborted);
    if (notes is null)
    {
        return Results.NotFound(new { error = "not found" });
    }

    return Results.Json(notes.Select(n => new
    {
        id = n.Id,
        text = n.Text,
        createdAt = n.CreatedAt
    }));
});

// POST /sessions/{id}/notes -> add a note to a session
app.MapPost("/sessions/{id}/notes", async (string id, SessionNoteCreateDto input, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    if (string.IsNullOrWhiteSpace(input.Text))
    {
        return Results.BadRequest(new { error = "text_required" });
    }

    var note = await sessionsRepository.AddNoteAsync(id, me, input.Text, ctx.RequestAborted);
    if (note is null)
    {
        return Results.NotFound(new { error = "not found" });
    }

    return Results.Json(new
    {
        id = note.Id,
        text = note.Text,
        createdAt = note.CreatedAt
    });
});

// PATCH /sessions/{id}/notes/{noteId} -> update a note
app.MapPatch("/sessions/{id}/notes/{noteId:long}", async (string id, long noteId, SessionNoteCreateDto input, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    if (string.IsNullOrWhiteSpace(input.Text))
    {
        return Results.BadRequest(new { error = "text_required" });
    }

    var updated = await sessionsRepository.UpdateNoteAsync(id, me, noteId, input.Text, ctx.RequestAborted);
    if (updated is null)
    {
        return Results.NotFound(new { error = "not found" });
    }

    if (!updated.Value)
    {
        return Results.NotFound(new { error = "note_not_found" });
    }

    return Results.Json(new
    {
        id = noteId,
        text = input.Text
    });
});

// DELETE /sessions/{id}/notes/{noteId} -> delete a note
app.MapDelete("/sessions/{id}/notes/{noteId:long}", async (string id, long noteId, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";

    var deleted = await sessionsRepository.DeleteNoteAsync(id, me, noteId, ctx.RequestAborted);
    if (deleted is null)
    {
        return Results.NotFound(new { error = "not found" });
    }

    if (!deleted.Value)
    {
        return Results.NotFound(new { error = "note_not_found" });
    }

    return Results.Ok(new { id = noteId, deleted = true });
});

// PATCH /uploads/{uploadId}/name -> rename a case for the current user
app.MapPatch("/uploads/{uploadId:guid}/name", async (Guid uploadId, RenameUploadDto input, HttpContext ctx) =>
{
    var me = ctx.GetCurrentUserId() ?? "";

    if (string.IsNullOrWhiteSpace(input.Name))
    {
        return Results.BadRequest(new { error = "name_required" });
    }

    using var conn = databaseOptions.CreateConnection();
    await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        UPDATE Uploads
        SET OriginalFileName = @name
        WHERE UploadId = @u AND UserId = @me";
    cmd.AddWithValue("@name", input.Name.Trim());
    cmd.AddWithValue("@u", uploadId);
    cmd.AddWithValue("@me", me);

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

app.MapGet("/uploads/{uploadId:guid}/download", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, IUploadRepository uploadsRepository) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
    if (!await uploadsRepository.CanAccessAsync(uploadId, me, ctx.RequestAborted)) return Results.NotFound();

    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
    var path = Path.Combine(uploadsRoot, $"{uploadId}.pdf");
    if (!File.Exists(path)) return Results.NotFound();

    string fileName = $"{uploadId}.pdf";
    if (uploadsRepository is not null)
    {
        var resolved = await uploadsRepository.GetDisplayNameAsync(uploadId, ctx.RequestAborted);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            fileName = resolved;
        }
    }

    return Results.File(path, "application/pdf", fileDownloadName: fileName, enableRangeProcessing: true);
});



// DELETE /uploads/{uploadId} -> delete a case and its sessions/messages/notes/files for current user
app.MapDelete("/uploads/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, IUploadRepository uploadsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    var id = uploadId.ToString(); // string version used for files / notes
    var sessionIds = await uploadsRepository.DeleteOwnedAsync(uploadId, me, ctx.RequestAborted);
    if (sessionIds is null)
    {
        return Results.NotFound(new { error = "not_found" });
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
app.MapDelete("/sessions/{sessionId}", async (string sessionId, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId() ?? "";
    if (string.IsNullOrEmpty(me))
    {
        return Results.Unauthorized();
    }

    if (!await sessionsRepository.DeleteSessionAsync(sessionId, me, ctx.RequestAborted))
    {
        return Results.NotFound(new { error = "session not found" });
    }

    return Results.NoContent();
});


// --- Admin: list all sessions for supervision (superuser only) ---
app.MapGet("/admin/sessions", async (HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId();
    var isSuper = ctx.IsCurrentUserSuperUser();

    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    if (!isSuper)
    {
        return Results.Forbid();
    }

    var query = ctx.Request.Query;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], Pagination.DefaultPageSize);
    var search = query.TryGetValue("q", out var qValue) ? qValue.ToString() : null;

    return Results.Ok(await sessionsRepository.ListAdminSessionsAsync(me, page, pageSize, search, ctx.RequestAborted));
});


// --- Admin: get details + messages for a specific session (superuser only) ---
app.MapGet("/admin/sessions/{sessionId}", async (string sessionId, HttpContext ctx, ISessionRepository sessionsRepository) =>
{
    var me = ctx.GetCurrentUserId();
    var isSuper = ctx.IsCurrentUserSuperUser();

    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    if (!isSuper)
    {
        return Results.Forbid();
    }

    var detail = await sessionsRepository.GetAdminSessionAsync(sessionId, me, ctx.RequestAborted);
    if (detail is null)
    {
        return Results.NotFound(new { error = "session not found" });
    }
    return Results.Ok(detail);
});




app.MapPost("/classes", async (HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (me == null) return Results.Unauthorized();

    var body = await ctx.Request.ReadFromJsonAsync<ClassCreateDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
    {
        return Results.BadRequest(new { error = "Missing class name" });
    }

    var created = await classesRepository.CreateAsync(me, body.Name, body.Description, ctx.RequestAborted);

    return Results.Ok(new
    {
        id = created.Id,
        name = created.Name,
        description = created.Description,
        joinCode = created.JoinCode,
        instructorId = created.InstructorId,
        createdAt = created.CreatedAt
    });
});

app.MapGet("/classes/mine", async (HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var query = ctx.Request.Query;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], Pagination.DefaultPageSize);

    var classes = await classesRepository.ListMineAsync(me, ctx.RequestAborted);
    var items = classes.Select(c => new
    {
        id = c.Id,
        name = c.Name,
        description = c.Description,
        joinCode = c.JoinCode,
        createdAt = c.CreatedAt,
        studentCount = c.StudentCount,
        caseCount = c.CaseCount
    }).ToList();
    var skip = Pagination.Offset(page, pageSize);
    var pageItems = items.Skip(skip).Take(pageSize).ToList();
    return Results.Ok(new PagedResult<object>(pageItems.Cast<object>().ToList(), page, pageSize, items.Count));
});

app.MapPost("/classes/join", async (HttpContext ctx, IClassRepository classesRepository) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var body = await ctx.Request.ReadFromJsonAsync<ClassJoinDto>();
    var joinCode = NormalizeJoinCode(body?.JoinCode);
    if (string.IsNullOrWhiteSpace(joinCode))
    {
        return Results.BadRequest(new { error = "Missing joinCode" });
    }

    var result = await classesRepository.JoinByCodeAsync(me, joinCode, ctx.RequestAborted);
    if (!result.UserFound) return Results.Unauthorized();
    if (result.UserIsInstructor) return Results.BadRequest(new { error = "Instructor accounts cannot join classes as students" });
    if (!result.ClassFound) return Results.NotFound(new { error = "Invalid class join code" });

    return Results.Ok(new
    {
        classId = result.ClassId,
        className = result.ClassName,
        joinCode,
        joined = true
    });
});

app.MapGet("/classes/enrolled", async (HttpContext ctx, IClassRepository classesRepository) =>
{
    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var query = ctx.Request.Query;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], Pagination.DefaultPageSize);

    var records = await classesRepository.ListEnrolledAsync(me, ctx.RequestAborted);
    var classes = records.Select(record => new EnrolledClassDto
    {
        Id = record.Id,
        Name = record.Name,
        Description = record.Description,
        JoinCode = record.JoinCode,
        CreatedAt = record.CreatedAt,
        JoinedAt = record.JoinedAt,
        InstructorName = record.InstructorName,
        InstructorEmail = record.InstructorEmail,
        CaseCount = record.Cases.Count,
        Cases = record.Cases.Select(c => new EnrolledClassCaseDto
        {
            UploadId = c.UploadId,
            FileName = c.FileName,
            Objective = c.Objective,
            Focus = c.Focus,
            DueAt = c.DueAt,
            AssignedAt = c.AssignedAt
        }).ToList()
    }).ToList();

    var skip = Pagination.Offset(page, pageSize);
    var pageItems = classes.Skip(skip).Take(pageSize).ToList();
    return Results.Ok(new PagedResult<object>(pageItems.Cast<object>().ToList(), page, pageSize, classes.Count));
});

app.MapDelete("/classes/{classId}", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    if (!await classesRepository.DeleteAsync(classId, me, ctx.RequestAborted))
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(new { classId, deleted = true });
});

app.MapGet("/classes/{classId}/join-code", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var record = await classesRepository.GetOrCreateJoinCodeAsync(classId, me, ctx.RequestAborted);
    if (record is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(new
    {
        classId,
        className = record.ClassName,
        joinCode = record.JoinCode
    });
});

app.MapPost("/classes/{classId}/join-code/regenerate", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var record = await classesRepository.RegenerateJoinCodeAsync(classId, me, ctx.RequestAborted);
    if (record is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(new
    {
        classId,
        className = record.ClassName,
        joinCode = record.JoinCode,
        regenerated = true
    });
});


app.MapPost("/classes/{classId}/students", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var body = await ctx.Request.ReadFromJsonAsync<AddStudentToClassDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.StudentEmail))
    {
        return Results.BadRequest(new { error = "Missing studentEmail" });
    }

    var result = await classesRepository.AddStudentAsync(classId, me, body.StudentEmail, ctx.RequestAborted);
    if (!result.ClassFound)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }
    if (!result.StudentFound)
    {
        return Results.NotFound(new { error = "No user found with that email" });
    }
    if (result.StudentIsInstructor)
    {
        return Results.BadRequest(new { error = "Instructor accounts cannot be added as students" });
    }
    if (result.AlreadyInClass)
    {
        return Results.Ok(new
        {
            classId,
            studentId = result.StudentId,
            alreadyInClass = true
        });
    }

    return Results.Ok(new
    {
        classId,
        studentId = result.StudentId,
        added = true
    });
});

app.MapDelete("/classes/{classId}/students/{studentId}", async (string classId, string studentId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var result = await classesRepository.RemoveStudentAsync(classId, me, studentId, ctx.RequestAborted);
    if (!result.ClassFound)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(new
    {
        classId,
        studentId,
        removed = result.Removed
    });
});


app.MapPost("/classes/{classId}/cases", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var body = await ctx.Request.ReadFromJsonAsync<AssignCaseToClassDto>();
    if (body == null || string.IsNullOrWhiteSpace(body.UploadId))
    {
        return Results.BadRequest(new { error = "Missing uploadId" });
    }

    var objective = NormalizeAssignmentObjective(body.Objective);
    var focus = NormalizeAssignmentFocus(body.Focus);
    var dueAt = NormalizeAssignmentDueAt(body.DueAt);
    var readingCoachQuestions = NormalizeReadingCoachQuestions(body.ReadingCoachQuestions);

    var result = await classesRepository.AssignCaseAsync(classId, me, body.UploadId, objective, focus, dueAt, readingCoachQuestions, ctx.RequestAborted);
    if (!result.ClassFound)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }
    if (!result.UploadFound)
    {
        return Results.NotFound(new { error = "Upload not found or not owned by you" });
    }
    if (result.AlreadyAssigned)
    {
        return Results.Ok(new
        {
            classId,
            uploadId = result.UploadId,
            objective = result.Objective,
            focus = result.Focus,
            dueAt = result.DueAt,
            readingCoachQuestions = result.ReadingCoachQuestions,
            alreadyAssigned = true,
            updated = result.Updated
        });
    }

    return Results.Ok(new
    {
        classId,
        uploadId = result.UploadId,
        objective = result.Objective,
        focus = result.Focus,
        dueAt = result.DueAt,
        readingCoachQuestions = result.ReadingCoachQuestions,
        assigned = true
    });
});

app.MapDelete("/classes/{classId}/cases/{uploadId}", async (string classId, string uploadId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
    {
        return Results.Unauthorized();
    }

    var result = await classesRepository.RemoveCaseAsync(classId, me, uploadId, ctx.RequestAborted);
    if (!result.ClassFound)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(new
    {
        classId,
        uploadId,
        removed = result.Removed
    });
});


app.MapGet("/classes/{classId}/details", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var details = await classesRepository.GetDetailsAsync(classId, me, ctx.RequestAborted);
    if (details is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(details);
});


app.MapGet("/classes/{classId}/history", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    // 0) Only instructors can call this
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    // Optional filters from query string
    var query = ctx.Request.Query;
    var studentId = query.ContainsKey("studentId") ? query["studentId"].ToString() : null;
    var uploadId = query.ContainsKey("uploadId") ? query["uploadId"].ToString() : null;
    var page = ParsePositiveInt(query["page"], 1);
    var pageSize = ParsePositiveInt(query["pageSize"], 50);

    var history = await classesRepository.GetHistoryAsync(classId, me, studentId, uploadId, page, pageSize, ctx.RequestAborted);
    if (history is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(history);
});


app.MapGet("/sessions/{sessionId}/messages", async (string sessionId, HttpContext ctx, IClassRepository classesRepository) =>
{
    // Only instructors may view session logs
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var instructorId = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(instructorId))
        return Results.Unauthorized();

    var result = await classesRepository.GetInstructorSessionLogAsync(sessionId, instructorId, ctx.RequestAborted);
    if (!result.SessionFound)
    {
        return Results.NotFound(new { error = "Session not found" });
    }
    if (!result.Authorized || result.Log is null)
    {
        return Results.Forbid();
    }

    // Final response:
    return Results.Ok(result.Log);
});

app.MapPost("/sessions/start", async (HttpContext ctx, IUploadRepository uploadsRepository, ISessionRepository sessionsRepository) =>
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

    var userId = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    if (!Guid.TryParse(uploadId, out var parsedUploadId) || !await uploadsRepository.CanAccessAsync(parsedUploadId, userId, ctx.RequestAborted))
    {
        return Results.NotFound(new { error = "not found" });
    }

    if (!string.IsNullOrWhiteSpace(classId))
    {
        if (!await uploadsRepository.CanAccessClassAssignmentAsync(parsedUploadId, userId, ctx.RequestAborted))
        {
            return Results.NotFound(new { error = "class assignment not found" });
        }
    }
    else
    {
        classId = await uploadsRepository.FindAccessibleClassIdAsync(parsedUploadId, userId, ctx.RequestAborted);
    }

    var newSessionId = Guid.NewGuid().ToString("N");

    await sessionsRepository.CreateAsync(newSessionId, userId, uploadId, DateTime.UtcNow, classId, ctx.RequestAborted);

    return Results.Ok(new { sessionId = newSessionId });
});


app.MapGet("/classes/{classId}/students", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    // Only instructors can view the students in a class
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var students = await classesRepository.ListStudentsAsync(classId, me, ctx.RequestAborted);
    if (students is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(students);
});

app.MapGet("/classes/{classId}/cases", async (string classId, HttpContext ctx, IClassRepository classesRepository) =>
{
    // Ensure only instructors can view class cases
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var me = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(me))
        return Results.Unauthorized();

    var cases = await classesRepository.ListCasesAsync(classId, me, ctx.RequestAborted);
    if (cases is null)
    {
        return Results.NotFound(new { error = "Class not found or not owned by you" });
    }

    return Results.Ok(cases);
});

// INSTRUCTOR: view full message history of a student's session
app.MapGet("/classes/{classId}/sessions/{sessionId}", async (string classId, string sessionId, HttpContext ctx, IClassRepository classesRepository) =>
{
    // 1) Only instructors can call this
    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var instructorId = ctx.GetCurrentUserId();
    if (string.IsNullOrWhiteSpace(instructorId))
        return Results.Unauthorized();

    var log = await classesRepository.GetClassSessionLogAsync(classId, sessionId, instructorId, ctx.RequestAborted);
    if (log is null)
    {
        return Results.NotFound(new { error = "Session not found for this class" });
    }

    return Results.Ok(log);
});









app.MapGet("/debug/session-access/{sessionId}", async (string sessionId, HttpContext ctx) =>
{
    if (!DebugEndpointsEnabled()) return Results.NotFound();

    var deny = RequireInstructor(ctx);
    if (deny != null) return deny;

    var instructorId = ctx.GetCurrentUserId();

    using var conn = databaseOptions.CreateConnection();
    await conn.OpenAsync();

    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT COUNT(*)
        FROM Sessions s
        JOIN ClassStudents cs ON cs.StudentId = s.UserId
        JOIN ClassCases cc ON cc.ClassId = cs.ClassId
        JOIN Classes c ON c.Id = cs.ClassId
        WHERE s.Id = @sessionId
          AND c.InstructorId = @instructorId;
    ";
    cmd.AddWithValue("@sessionId", sessionId);
    cmd.AddWithValue("@instructorId", instructorId ?? "");

    var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    return Results.Ok(new { instructorId, sessionId, count });
});

app.MapFallbackToFile("index.html");

app.Run();






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

static string NormalizeJoinCode(string? code)
{
    if (string.IsNullOrWhiteSpace(code)) return "";
    return Regex.Replace(code.Trim().ToUpperInvariant(), "[^A-Z0-9]", "");
}

static int ParsePositiveInt(string? value, int fallback)
{
    if (int.TryParse(value, out var parsed) && parsed > 0)
    {
        return parsed;
    }

    return fallback;
}

static string CleanTrackedQuestion(string? question)
{
    if (string.IsNullOrWhiteSpace(question)) return "";

    var text = question.Trim();
    text = Regex.Replace(text, "%\\s+", " ");
    text = Regex.Replace(text, "\\s+", " ");
    return text.Trim();
}

static string? NormalizeAssignmentObjective(string? objective)
{
    if (string.IsNullOrWhiteSpace(objective)) return null;

    var text = Regex.Replace(objective.Trim(), "\\s+", " ");
    return text.Length <= 600 ? text : text[..600].TrimEnd();
}

static string? NormalizeAssignmentFocus(string? focus)
{
    if (string.IsNullOrWhiteSpace(focus)) return null;

    var normalized = Regex.Replace(focus.Trim().ToLowerInvariant(), "[^a-z0-9_-]", "_");
    normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized[..Math.Min(normalized.Length, 80)];
}

static string? NormalizeAssignmentDueAt(string? dueAt)
{
    if (string.IsNullOrWhiteSpace(dueAt)) return null;
    return DateTimeOffset.TryParse(dueAt.Trim(), out var parsed)
        ? parsed.UtcDateTime.ToString("O")
        : dueAt.Trim();
}

static string? NormalizeReadingCoachQuestions(string? readingCoachQuestions)
{
    if (string.IsNullOrWhiteSpace(readingCoachQuestions)) return null;

    var text = Regex.Replace(readingCoachQuestions.Trim(), "\\s+", " ");
    return text.Length <= 2000 ? text : text[..2000].TrimEnd();
}

public enum QuestionType
{
    Summary,    // "What is this paper about?", "Give an overview"
    Fact,       // "Who are the authors?", "When was this published?"
    Method,     // "What method did they use?", "How did they collect data?"
    Findings,   // "What did they find?", "What are the main results?"
    WhyExplain, // "Why did they choose this?", "Explain this in simpler terms"
    Other       // Anything else
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
public record ClassJoinDto(string JoinCode);
public record AddStudentToClassDto(string StudentEmail);

public record AssignCaseToClassDto(string UploadId, string? Objective = null, string? Focus = null, string? DueAt = null, string? ReadingCoachQuestions = null);

public sealed class EnrolledClassDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? JoinCode { get; set; }
    public string CreatedAt { get; set; } = "";
    public string JoinedAt { get; set; } = "";
    public string InstructorName { get; set; } = "";
    public string InstructorEmail { get; set; } = "";
    public int CaseCount { get; set; }
    public List<EnrolledClassCaseDto> Cases { get; set; } = new();
}

public sealed class EnrolledClassCaseDto
{
    public string UploadId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Objective { get; set; }
    public string? Focus { get; set; }
    public string? DueAt { get; set; }
    public string AssignedAt { get; set; } = "";
}

public sealed class SessionMineDto
{
    public string SessionId { get; set; } = "";
    public string? UploadId { get; set; }
    public string CaseName { get; set; } = "Untitled case";
    public string? CreatedAt { get; set; }
    public string? LastActivityAt { get; set; }
    public int DurationSec { get; set; }
    public int MessageCount { get; set; }
    public int NotesCount { get; set; }
    public string? LastMessagePreview { get; set; }
}









public record CaseDto(string Id, string Name, int Pages, int Images, double SizeMB, string UploadedAt);

























