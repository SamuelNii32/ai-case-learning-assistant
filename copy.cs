//using Microsoft.AspNetCore.Builder;
//using Microsoft.AspNetCore.Http;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using System;
//using System.IO;
//using System.Text.Json;
//using OpenAI.Chat;
//using OpenAI.Responses;
//using System.Linq;
//using OpenAI.Embeddings;
//using UglyToad.PdfPig;
//using UglyToad.PdfPig.Content;
//using System.Numerics.Tensors;
//using System.Text.RegularExpressions;
//using PdfPigDoc = UglyToad.PdfPig.PdfDocument;






//// iText7 for page count + raster image counting
//using iText.Kernel.Pdf;
//using iText.Kernel.Pdf.Canvas.Parser;
//using iText.Kernel.Pdf.Canvas.Parser.Data;
//using iText.Kernel.Pdf.Canvas.Parser.Listener;

//var builder = WebApplication.CreateBuilder(args);

//// OpenAI Chat client (small model to keep costs low)
//builder.Services.AddSingleton<ChatClient>(_ =>
//{
//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");
//    return new ChatClient(model: "gpt-4o-mini", apiKey);
//});


//// Swagger (optional)
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//// CORS for your frontend
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("FrontendDev", p => p
//        .WithOrigins("http://localhost:5174", "http://localhost:3000")
//        .AllowAnyHeader()
//        .AllowAnyMethod());
//});



//var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseCors("FrontendDev");
//// app.UseHttpsRedirection();

//app.MapGet("/ping", () => Results.Ok("pong"));

//// POST /uploads  (save PDF + minimal summary) — uses ABSOLUTE uploads path
//app.MapPost("/uploads", async (HttpRequest request, IWebHostEnvironment env) =>
//{
//    if (!request.HasFormContentType)
//        return Results.BadRequest("Use multipart/form-data.");

//    var form = await request.ReadFormAsync();
//    var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
//    if (file is null || file.Length == 0)
//    {
//        Console.WriteLine($"[UPLOAD DEBUG] ContentType={request.ContentType} Keys=[{string.Join(",", form.Keys)}] Files={form.Files.Count}");
//        return Results.BadRequest($"No file. ContentType={request.ContentType}; Keys=[{string.Join(",", form.Keys)}]; Files={form.Files.Count}");
//    }

//    // PDF-only guard
//    var isPdf = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
//                || Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
//    if (!isPdf)
//        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

//    var uploadId = Guid.NewGuid();

//    // ABSOLUTE uploads folder
//    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
//    Directory.CreateDirectory(uploadsRoot);

//    var filePath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");

//    // Save file
//    await using (var outStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
//    {
//        await file.CopyToAsync(outStream);
//    }

//    // --- Minimal analysis: pages + raster images + file size + uploadedAt ---
//    var uploadedAt = DateTime.UtcNow;

//    var fi = new FileInfo(filePath);
//    long fileSizeBytes = fi.Length;
//    double fileSizeMB = Math.Round(fileSizeBytes / (1024.0 * 1024.0), 2);

//    int pages;
//    using (var doc = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(filePath)))
//    {
//        pages = doc.GetNumberOfPages();
//    }

//    int images = PdfImageUtils.CountRasterImagesExact(filePath);

//    var summary = new
//    {
//        uploadId,
//        fileName = file.FileName,
//        fileSizeBytes,
//        fileSizeMB,
//        pages,
//        counts = new { images },
//        uploadedAt = uploadedAt.ToString("o"),
//        generatedAt = DateTime.UtcNow.ToString("o")
//    };

//    var summaryPath = Path.Combine(uploadsRoot, $"{uploadId}.summary.json");
//    await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary));

//    return Results.Json(new { uploadId });
//})
//.Accepts<IFormFile>("multipart/form-data")
//.Produces(StatusCodes.Status200OK)
//.Produces(StatusCodes.Status400BadRequest)
//.Produces(StatusCodes.Status415UnsupportedMediaType);

//// GET /uploads/{id}/summary — reads from ABSOLUTE path
//app.MapGet("/uploads/{uploadId:guid}/summary", async (Guid uploadId, IWebHostEnvironment env) =>
//{
//    var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.summary.json");
//    if (!File.Exists(path)) return Results.NotFound();
//    var json = await File.ReadAllTextAsync(path);
//    return Results.Text(json, "application/json");
//});

//// GET /cases — scans ABSOLUTE uploads folder
//app.MapGet("/cases", (IWebHostEnvironment env) =>
//{
//    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
//    Directory.CreateDirectory(uploadsRoot);

//    var cases = new List<CaseDto>();

//    foreach (var path in Directory.EnumerateFiles(uploadsRoot, "*.summary.json"))
//    {
//        try
//        {
//            using var fs = File.OpenRead(path);
//            using var doc = JsonDocument.Parse(fs);
//            var root = doc.RootElement;

//            string id = root.TryGetProperty("uploadId", out var pid)
//                ? (pid.ValueKind == JsonValueKind.String ? pid.GetString()! : pid.ToString())
//                : "";
//            if (string.IsNullOrWhiteSpace(id)) continue;

//            string name = root.TryGetProperty("fileName", out var pn) ? (pn.GetString() ?? "") : "";
//            int pages = root.TryGetProperty("pages", out var pp) && pp.TryGetInt32(out var p) ? p : 0;
//            double sizeMB = root.TryGetProperty("fileSizeMB", out var ps) && ps.TryGetDouble(out var s) ? s : 0.0;

//            int images = 0;
//            if (root.TryGetProperty("counts", out var counts) && counts.TryGetProperty("images", out var ci))
//                ci.TryGetInt32(out images);

//            string uploadedAt = root.TryGetProperty("uploadedAt", out var pu) && pu.ValueKind == JsonValueKind.String
//                ? (pu.GetString() ?? "")
//                : File.GetLastWriteTimeUtc(path).ToString("o");

//            cases.Add(new CaseDto(id, name, pages, images, sizeMB, uploadedAt));
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"[CASES] Skipping '{Path.GetFileName(path)}': {ex.GetType().Name} - {ex.Message}");
//        }
//    }

//    var ordered = cases
//        .OrderByDescending(c => DateTime.TryParse(c.UploadedAt, out var dt) ? dt : DateTime.MinValue)
//        .ToList();

//    return Results.Json(ordered);
//});

//// GET/HEAD /uploads/{id}.pdf — serves from ABSOLUTE path (use Results.File)
//app.MapMethods("/uploads/{uploadId:guid}.pdf", new[] { "GET", "HEAD" }, (Guid uploadId, IWebHostEnvironment env) =>
//{
//    try
//    {
//        var path = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
//        if (!File.Exists(path)) return Results.NotFound();
//        return Results.File(path, "application/pdf", enableRangeProcessing: true);
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"[PDF GET] {uploadId} failed: {ex.GetType().Name} - {ex.Message}");
//        return Results.StatusCode(500);
//    }
//});


//// DEV: simple SSE mock stream
//app.MapGet("/api/chat/stream", async (HttpContext ctx) =>
//{
//    // Required SSE headers
//    ctx.Response.Headers["Content-Type"] = "text/event-stream";
//    ctx.Response.Headers["Cache-Control"] = "no-cache, no-transform";
//    ctx.Response.Headers["Connection"] = "keep-alive";

//    // Kick the stream so proxies don’t buffer forever
//    await ctx.Response.WriteAsync("\n");
//    await ctx.Response.Body.FlushAsync();

//    var prompt = ctx.Request.Query["prompt"].ToString();

//    var text =
//        $"Thanks! I looked at your prompt ({(string.IsNullOrWhiteSpace(prompt) ? "…" : prompt)}) " +
//        "and found key evidence on page 5. Here’s a quick summary to get you started.";

//    var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
//    var delay = TimeSpan.FromMilliseconds(50);

//    try
//    {
//        foreach (var t in tokens)
//        {
//            if (ctx.RequestAborted.IsCancellationRequested) break;

//            // stream one token
//            await ctx.Response.WriteAsync($"event: token\ndata: {{\"text\":\"{t}\"}}\n\n");
//            await ctx.Response.Body.FlushAsync();

//            await Task.Delay(delay, ctx.RequestAborted);
//        }

//        if (!ctx.RequestAborted.IsCancellationRequested)
//        {
//            // one source chip (page 5), then done
//            await ctx.Response.WriteAsync("event: source\ndata: {\"page\":5,\"label\":\"p. 5\"}\n\n");
//            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
//            await ctx.Response.Body.FlushAsync();
//        }
//    }
//    catch (OperationCanceledException)
//    {
//        // client disconnected—ignore
//    }
//});

//// Figures/visuals for a document (MVP: stub data)
//// GET /api/documents/{caseId}/figures
//app.MapGet("/api/documents/{caseId}/figures", (string caseId) =>
//{
//    // TODO: Replace this stub with your real analysis lookup for `caseId`
//    // Shape: [{ id, page, type:"image", caption, bbox:null }]
//    var stub = new[]
//    {
//        new { id = $"{caseId}-p3-1",  page = 3,  type = "image", caption = "Visual on page 3",  bbox = (object?)null },
//        new { id = $"{caseId}-p7-1",  page = 7,  type = "image", caption = "Visual on page 7",  bbox = (object?)null },
//        new { id = $"{caseId}-p10-1", page = 10, type = "image", caption = "Visual on page 10", bbox = (object?)null },
//    };

//    return Results.Json(stub);
//});


//// GET /api/llm/ping — round-trip to model
//app.MapGet("/api/llm/ping", async (OpenAI.Chat.ChatClient chat) =>
//{
//    // Some installs return ClientResult<ChatCompletion>; take .Value to get ChatCompletion
//    var result = await chat.CompleteChatAsync("Reply exactly: hello from CasePilot Q&A");
//    var completion = result.Value;               // <-- the key fix
//    var text = completion.Content.Count > 0
//        ? completion.Content[0].Text ?? ""
//        : "";

//    return Results.Json(new { ok = text.Contains("hello from CasePilot Q&A"), reply = text });
//});

//// GET /api/embeddings/ping — sanity check: returns vector length
//// GET /api/embeddings/ping — sanity check: returns vector length
//app.MapGet("/api/embeddings/ping", () =>
//{
//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

//    var client = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);

//    var result = client.GenerateEmbedding("hello world");  // wrapper + value
//    var dims = result.Value.ToFloats().Length;            // <-- unwrap, then ToFloats()

//    return Results.Json(new { dims });
//});

//// ---- text extraction helper (PdfPig) ----
//static IEnumerable<(int page, string text)> ExtractPerPageText(string path)
//{
//    using var doc = UglyToad.PdfPig.PdfDocument.Open(path); // PdfPig
//    foreach (var page in doc.GetPages())
//    {
//        var txt = page.Text ?? string.Empty; // plain text per page
//        yield return (page.Number, txt);
//    }
//}

//// GET /uploads/{id}/pages/preview  -> returns first few page snippets (no embeddings yet)
//app.MapGet("/uploads/{uploadId:guid}/pages/preview", (Guid uploadId, IWebHostEnvironment env) =>
//{
//    var pdfPath = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
//    if (!System.IO.File.Exists(pdfPath)) return Results.NotFound();

//    var preview = ExtractPerPageText(pdfPath)
//        .Take(3)
//        .Select(p => new
//        {
//            page = p.page,
//            snippet = SafeHead(p.text, 300) + (p.text.Length > 300 ? "…" : "")
//        });

//    return Results.Json(preview);
//});


//// ---- simple in-memory vector index ----

//// POST /index/{uploadId} — embed per-page text into an in-memory index (and persist to disk)
//app.MapPost("/index/{uploadId:guid}", async (Guid uploadId, IWebHostEnvironment env) =>
//{
//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

//    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
//    var pdfPath = Path.Combine(uploadsRoot, $"{uploadId}.pdf");
//    if (!System.IO.File.Exists(pdfPath))
//        return Results.NotFound(new { error = "PDF not found" });

//    var emb = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
//    var chunks = new List<IndexedChunk>();
//    int pagesIndexed = 0;

//    using (var pdf = PdfPigDoc.Open(pdfPath))
//    {
//        foreach (var page in pdf.GetPages())
//        {
//            var text = (page.Text ?? "").Trim();
//            if (string.IsNullOrWhiteSpace(text)) continue;
//            pagesIndexed++;

//            foreach (var c in TextChunking.ChunkBySize(text, 1200, 200)) // larger, still safe
//            {
//                var vec = emb.GenerateEmbedding(c).Value.ToFloats();
//                var preview = c; // keep full chunk text (no truncation)
//                chunks.Add(new IndexedChunk(page.Number, vec, preview));
//            }

//        }
//    }

//    // store in memory
//    InMemoryStore.VectorIndex[uploadId.ToString()] = chunks;

//    // persist to disk
//    Directory.CreateDirectory(uploadsRoot);
//    var serializable = chunks.Select(c => new SerializableChunk(c.Page, c.Preview, c.Vec.ToArray())).ToArray();
//    var indexPath = Path.Combine(uploadsRoot, $"{uploadId}.index.json");
//    await System.IO.File.WriteAllTextAsync(indexPath, System.Text.Json.JsonSerializer.Serialize(serializable));

//    return Results.Json(new
//    {
//        uploadId,
//        chunks = chunks.Count,
//        pagesIndexed,
//        sample = chunks.Take(3).Select(x => new { page = x.Page, preview = x.Preview })
//    });
//});



//// GET /search/{uploadId}?q=...  -> top-k chunks by cosine similarity
//app.MapGet("/search/{uploadId:guid}", (Guid uploadId, string q, IWebHostEnvironment env) =>
//{
//    // Lazy-load index from disk if missing in RAM
//    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
//    {
//        if (!IndexPersistence.TryLoad(uploadId, env, out list))
//            return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });
//    }

//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

//    var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
//    var qVec = embClient.GenerateEmbedding(q ?? string.Empty).Value.ToFloats();

//    var scored = list
//        .Select(x => new
//        {
//            x.Page,
//            x.Preview,
//            score = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(qVec.Span, x.Vec.Span)
//        })
//        .OrderByDescending(s => s.score)
//        .Take(5)
//        .ToList();

//    return Results.Json(scored);
//});


//// GET /ask/{uploadId}?q=...
//app.MapGet("/ask/{uploadId:guid}", (Guid uploadId, string q, IWebHostEnvironment env) =>
//{
//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

//    // Ensure index is loaded (lazy-load from disk if needed)
//    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
//    {
//        if (!IndexPersistence.TryLoad(uploadId, env, out list))
//            return Results.NotFound(new { error = "Not indexed. POST /index/{uploadId} first." });
//    }

//    try
//    {
//        // Embed the query
//        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
//        var qVec = embClient.GenerateEmbedding(q ?? string.Empty).Value.ToFloats();

//        // Retrieval (hybrid + MMR + list-aware)
//        var top = QaRetrieval.SelectTop(list, qVec.Span, q ?? "", forStreaming: false);


//        // --- AUTO-ESCALATE: if the query looks list-like but retrieval is thin, widen with lexical fallback ---
//        if (QaRetrieval.IsListy(q ?? "") && (top.Count < 3))
//        {
//            var fbWide = QaRetrieval.KeywordFallback(list, q ?? "", k: 12);
//            if (fbWide.Count > top.Count)
//            {
//                var ctxStrWide = string.Join("\n\n", fbWide.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
//                return AnswerWithContext(ctxStrWide, q, fbWide.Select(t => t.Page).Distinct().ToArray(), apiKey);
//            }
//        }
//        // --- END AUTO-ESCALATE ---


//        // Confidence gate (keep modest so we don't block obvious answers)
//        const float THRESHOLD = 0.15f;
//        var bestScore = top.Count > 0 ? top.Max(t => t.Score) : 0f;

//        if (top.Count == 0 || bestScore < THRESHOLD)
//        {
//            // Fallback: keyword scan block
//            var fb = QaRetrieval.KeywordFallback(list, q ?? "", k: 8);
//            if (fb.Count == 0)
//            {
//                return Results.Json(new
//                {
//                    answer = "I can't find that in the document.",
//                    citations = Array.Empty<int>(),
//                    pagesUsed = Array.Empty<int>(),
//                    retrieval = new { bestScore, threshold = THRESHOLD }
//                });
//            }

//            var ctxStrFb = string.Join("\n\n", fb.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
//            return AnswerWithContext(ctxStrFb, q, fb.Select(t => t.Page).Distinct().ToArray(), apiKey);
//        }

//        var ctxStr = string.Join("\n\n", top.Select(t => $"— Page {t.Page} —\n{t.Preview}"));
//        return AnswerWithContext(ctxStr, q, top.Select(t => t.Page).Distinct().ToArray(), apiKey);
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"[ASK ERROR] {ex.GetType().Name}: {ex.Message}");
//        return Results.Json(new { error = ex.GetType().Name, message = ex.Message });
//    }

//    // local helper to call the model & format JSON
//    static IResult AnswerWithContext(string ctxStr, string question, int[] pages, string apiKeyLocal)
//    {
//        var chat = new OpenAI.Chat.ChatClient(model: "gpt-4o-mini", apiKeyLocal);

//        // Use explicit messages + a list-friendly system instruction
//        // make sure you have: using System.Collections.Generic; using OpenAI.Chat;

//        var messages = new List<OpenAI.Chat.ChatMessage>
//{
//    new OpenAI.Chat.SystemChatMessage(
//    "You are a precise assistant. Answer ONLY using the provided Context. " +
//    "If the answer is not in Context, reply exactly: I can't find that in the document. " +
//    "When listing, include ALL items found in Context; do not guess. " +
//    "When the user asks to list items of a specific category, include ONLY items that strictly match that category, " +
//    "and exclude closely related but different categories. " +
//    "Add page chips like [p:X] immediately after each relevant line."
//),

//    new OpenAI.Chat.UserChatMessage($@"Question: {question}

//Context:
//{ctxStr}")
//};

//        var result = chat.CompleteChat(messages).Value;



//        // Join ALL content parts safely (don't rely on [0])
//        var answer = string.Concat(result.Content.Select(part => part.Text ?? string.Empty)).Trim();

//        if (string.IsNullOrWhiteSpace(answer))
//        {
//            return Results.Json(new
//            {
//                answer = "I can't find that in the document.",
//                pagesUsed = pages,
//                citations = Array.Empty<int>(),
//                debug = new { note = "Empty model reply; joined all parts", contextPreview = ctxStr.Length > 300 ? ctxStr[..300] + "…" : ctxStr }
//            });
//        }

//        // Extract [p:X] chips
//        var citations = System.Text.RegularExpressions.Regex
//            .Matches(answer, @"\[\s*p\s*:\s*(\d+)\s*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
//            .Select(m => int.Parse(m.Groups[1].Value))
//            .Distinct()
//            .ToArray();

//        return Results.Json(new
//        {
//            answer,
//            pagesUsed = pages,
//            citations
//        });
//    }
//});



//// GET /ask/stream/{uploadId}?q=...  -> SSE: token-by-token answer + citations + done
//app.MapGet("/ask/stream/{uploadId:guid}", async (Guid uploadId, string q, HttpContext ctx, IWebHostEnvironment env) =>
//{
//    Console.WriteLine("[ASK v2] /ask/stream");

//    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//        ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

//    // SSE headers
//    ctx.Response.Headers["Content-Type"] = "text/event-stream";
//    ctx.Response.Headers["Cache-Control"] = "no-cache";
//    ctx.Response.Headers["Connection"] = "keep-alive";
//    await ctx.Response.WriteAsync("\n");
//    await ctx.Response.Body.FlushAsync();

//    // Ensure index is loaded (lazy-load from disk if needed)
//    if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var list) || list.Count == 0)
//    {
//        if (!IndexPersistence.TryLoad(uploadId, env, out list))
//        {
//            await ctx.Response.WriteAsync("event: error\ndata: {\"message\":\"Not indexed. POST /index first.\"}\n\n");
//            await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
//            await ctx.Response.Body.FlushAsync();
//            return;
//        }
//    }

//    try
//    {
//        var embClient = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
//        var qVec = embClient.GenerateEmbedding(q ?? string.Empty).Value.ToFloats();

//        // Unified retrieval (same as non-streaming)
//        var top = QaRetrieval.SelectTop(list, qVec.Span, q ?? "", forStreaming: true);

//        // Confidence gate (can be raised later; at demo time you might prefer answers over abstains)
//        const float THRESHOLD = 0.00f; // was 0.20f
//        var bestScore = top.Count > 0 ? top.Max(t => t.Score) : 0f;
//        Console.WriteLine($"[ASK v2] STREAM bestScore={bestScore:F3} threshold={THRESHOLD:F2} top={top.Count}");

//        // --- AUTO-ESCALATE: if list-like and thin, widen with lexical fallback ---
//        if (QaRetrieval.IsListy(q ?? "") && (top.Count < 3))
//        {
//            var fbWide = QaRetrieval.KeywordFallback(list, q ?? "", k: 12);
//            if (fbWide.Count > top.Count)
//            {
//                top = fbWide; // let the existing ctxChunks logic use the widened set
//            }
//        }
//        // --- END AUTO-ESCALATE ---


//        List<TopChunk> ctxChunks;
//        if (top.Count == 0)
//        {
//            var fb = QaRetrieval.KeywordFallback(list, q ?? "", k: 8);
//            if (fb.Count == 0)
//            {
//                await ctx.Response.WriteAsync("event: token\ndata: {\"text\":\"I can't find that in the document.\"}\n\n");
//                await ctx.Response.WriteAsync("event: citations\ndata: []\n\n");
//                await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
//                await ctx.Response.Body.FlushAsync();
//                return;
//            }
//            ctxChunks = fb;
//        }
//        else
//        {
//            ctxChunks = top;
//        }

//        // Build short context (bounds-safe preview)
//        var context = string.Join("\n\n", ctxChunks.Select(t =>
//    $"— Page {t.Page} —\n{t.Preview}"));


//        var chat = new OpenAI.Chat.ChatClient(model: "gpt-4o-mini", apiKey);
//        var prompt = $"""
//You are a precise assistant. Answer ONLY using the Context below.
//If the answer is not in Context, say: "I can't find that in the document."
//When listing, include **all** items you find in Context; don't guess.
//When the user asks to list items of a specific category, include ONLY items that strictly match that category, and exclude closely related but different categories.
//Add page chips like [p:X] after the sentence they belong to.
//Keep the answer concise.

//Question: {q}

//Context:
//{context}
//""";


//        var updates = chat.CompleteChatStreaming(prompt);
//        var sb = new System.Text.StringBuilder();

//        foreach (var update in updates)
//        {
//            if (ctx.RequestAborted.IsCancellationRequested) break;

//            if (update.ContentUpdate.Count > 0)
//            {
//                var piece = update.ContentUpdate[0].Text ?? "";
//                sb.Append(piece);
//                var json = System.Text.Json.JsonSerializer.Serialize(new { text = piece });
//                await ctx.Response.WriteAsync($"event: token\ndata: {json}\n\n");
//                await ctx.Response.Body.FlushAsync();
//            }
//        }

//        // Extract citations and finish
//        var answer = sb.ToString();
//        var pages = System.Text.RegularExpressions.Regex
//            .Matches(answer, @"\[\s*p\s*:\s*(\d+)\s*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
//            .Select(m => int.Parse(m.Groups[1].Value))
//            .Distinct()
//            .ToArray();

//        await ctx.Response.WriteAsync($"event: citations\ndata: {System.Text.Json.JsonSerializer.Serialize(pages)}\n\n");
//        await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
//        await ctx.Response.Body.FlushAsync();
//    }
//    catch (Exception ex)
//    {
//        var err = System.Text.Json.JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
//        await ctx.Response.WriteAsync($"event: error\ndata: {err}\n\n");
//        await ctx.Response.WriteAsync("event: done\ndata: {}\n\n");
//        await ctx.Response.Body.FlushAsync();
//    }
//});


//app.MapGet("/index/status/{uploadId:guid}", (Guid uploadId, IWebHostEnvironment env) =>
//{
//    var id = uploadId.ToString();
//    var inMemory = InMemoryStore.VectorIndex.TryGetValue(id, out var list) && list?.Count > 0;

//    var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
//    var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
//    var onDisk = System.IO.File.Exists(indexPath);

//    int? chunks = null;
//    if (onDisk && !inMemory)
//    {
//        try
//        {
//            var json = System.IO.File.ReadAllText(indexPath);
//            var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
//            chunks = rows?.Length;
//        }
//        catch { /* ignore */ }
//    }
//    else if (inMemory)
//    {
//        chunks = list!.Count;
//    }

//    return Results.Json(new { uploadId = id, inMemory, onDisk, chunks });
//});






//app.Run();

//// Safe, bounds-checked head-of-string helper
//static string SafeHead(string s, int max) =>
//    string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

//public record IndexedChunk(int Page, ReadOnlyMemory<float> Vec, string Preview);

//public static class InMemoryStore
//{
//    public static readonly Dictionary<string, List<IndexedChunk>> VectorIndex = new();
//}


//public record CaseDto(string Id, string Name, int Pages, int Images, double SizeMB, string UploadedAt);
//public record SerializableChunk(int Page, string Preview, float[] Vec);



//// ---------------- helpers ----------------
//static class PdfImageUtils
//{
//    private sealed class ImageCounterListener : IEventListener
//    {
//        public int Count { get; private set; }
//        public void EventOccurred(IEventData data, EventType type)
//        {
//            if (type == EventType.RENDER_IMAGE)
//                Count++;
//        }
//        public ICollection<EventType> GetSupportedEvents() => null;
//    }

//    public static int CountRasterImagesExact(string path)
//    {
//        using var pdf = new iText.Kernel.Pdf.PdfDocument(new PdfReader(path));
//        int total = 0;
//        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
//        {
//            var listener = new ImageCounterListener();
//            var processor = new PdfCanvasProcessor(listener);
//            processor.ProcessPageContent(pdf.GetPage(i));
//            total += listener.Count;
//        }
//        return total;
//    }
//}
//public static class TextChunking
//{
//    public static IEnumerable<string> ChunkBySize(string text, int maxChars = 1000, int overlap = 160)
//    {
//        if (string.IsNullOrEmpty(text)) yield break;
//        if (maxChars <= 0) yield break;
//        if (overlap < 0) overlap = 0;
//        int i = 0;
//        while (i < text.Length)
//        {
//            int end = Math.Min(text.Length, i + maxChars);
//            int softEnd = end;
//            for (int j = end - 1; j > i + maxChars / 2; j--)
//            {
//                char c = text[j];
//                if (c == '.' || c == '!' || c == '?' || char.IsWhiteSpace(c)) { softEnd = j + 1; break; }
//            }
//            end = softEnd;
//            var slice = text.AsSpan(i, end - i).ToString().Trim();
//            if (!string.IsNullOrWhiteSpace(slice)) yield return slice;
//            if (end >= text.Length) yield break;
//            i = Math.Max(end - overlap, i + 1);
//        }
//    }
//}



//public static class IndexPersistence
//{
//    public static bool TryLoad(Guid uploadId, IWebHostEnvironment env, out List<IndexedChunk> list)
//    {
//        var id = uploadId.ToString();
//        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
//        var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
//        list = null!;

//        if (!File.Exists(indexPath)) return false;

//        var json = File.ReadAllText(indexPath);
//        var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
//        if (rows is null || rows.Length == 0) return false;

//        list = rows.Select(r => new IndexedChunk(
//            Page: r.Page,
//            Vec: new ReadOnlyMemory<float>(r.Vec),
//            Preview: r.Preview
//        )).ToList();

//        InMemoryStore.VectorIndex[id] = list;
//        return true;
//    }
//}

//// Return shape used by both routes
//public record TopChunk(int Page, string Preview, float Score);

//public static class QaRetrieval
//{
//    // --- Query understanding ---
//    public static bool IsListy(string q)
//    {
//        var s = q ?? string.Empty;

//        // Common list verbs & phrasings
//        if (Regex.IsMatch(s, @"\b(list|all|which|enumerate|show|show me|give|give me|name|return|extract|identify|find all|find every|every|provide|report|catalog|compile|what\s+are|what\s+were)\b", RegexOptions.IgnoreCase))
//            return true;

//        // Numeric / date cues (often imply multiple items)
//        if (Regex.IsMatch(s, @"[%+]", RegexOptions.IgnoreCase)) return true;                                     // %, +
//        if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b", RegexOptions.IgnoreCase)) return true;                    // years
//        if (Regex.IsMatch(s, @"\b(date|dates|range|ranges|deadline|deadlines)\b", RegexOptions.IgnoreCase)) return true;
//        if (Regex.IsMatch(s, @"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b", RegexOptions.IgnoreCase))
//            return true;

//        // Category words that commonly yield lists
//        if (Regex.IsMatch(s, @"\b(languages?|frameworks?|libraries|databases?|tools?|certifications?|people|persons|authors?|organizations?|countries|requirements?|risks?|achievements?|metrics?|publications?|references?)\b", RegexOptions.IgnoreCase))
//            return true;

//        return false;
//    }


//    // Tokenize to alnum lowercase
//    private static string[] Tokens(string s)
//    {
//        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
//        return Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]{2,}")
//                    .Select(m => m.Value)
//                    .ToArray();
//    }

//    // Lightweight lexical score (no external engine)
//    private static float LexicalScore(string preview, HashSet<string> qset)
//    {
//        if (string.IsNullOrEmpty(preview) || qset.Count == 0) return 0f;
//        var p = preview.ToLowerInvariant();

//        float s = 0f;
//        foreach (var t in qset) if (p.Contains(t)) s += 1f;

//        if (p.Contains("@")) s += 0.5f;          // emails
//        if (Regex.IsMatch(p, @"\b\d{4}\b")) s += 0.25f; // years/dates
//        return s;
//    }

//    // Tiny presence boost (kept small)
//    private static float Boost(string preview, HashSet<string> qset)
//    {
//        var p = preview?.ToLowerInvariant() ?? "";
//        float b = 0f;
//        foreach (var t in qset)
//        {
//            if (p.Contains(t)) { b += 0.03f; if (b >= 0.09f) break; }
//        }
//        if (p.Contains("@")) b += 0.02f;
//        return Math.Min(b, 0.10f);
//    }

//    // Fallback: keyword scan to grab a broader block (generic, not resume-specific)
//    public static List<TopChunk> KeywordFallback(List<IndexedChunk> list, string q, int k = 8)
//    {
//        var qset = new HashSet<string>(Tokens(q));
//        if (qset.Count == 0) return new List<TopChunk>();

//        return list
//            .Select(x => new { x.Page, x.Preview, lex = LexicalScore(x.Preview, qset) })
//            .Where(r => r.lex > 0)
//            .OrderByDescending(r => r.lex)
//            .Take(k)
//            .Select(r => new TopChunk(r.Page, r.Preview, 0.16f))
//            .ToList();
//    }


//    // Main selection with hybrid score + MMR + list-mode + optional page dedupe
//    public static List<TopChunk> SelectTop(
//     List<IndexedChunk> list,
//     ReadOnlySpan<float> qVec,
//     string q,
//     bool forStreaming)
//    {
//        bool listy = IsListy(q);

//        // More coverage for lists; conservative for non-lists
//        int K = listy ? 12 : (forStreaming ? 3 : 3);

//        var qset = new HashSet<string>(Tokens(q));
//        const float alpha = 0.85f; // embedding weight
//        const float beta = 0.15f; // lexical weight

//        // Avoid ref-like span use in lambdas
//        float[] qVecArr = qVec.ToArray();

//        // 1) score candidates (oversample before MMR)
//        var cands = list.Select(x =>
//        {
//            var cos = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(qVecArr, x.Vec.Span);
//            var lex = LexicalScore(x.Preview, qset);
//            var boo = Boost(x.Preview, qset);
//            var fin = alpha * cos + beta * lex + boo;
//            return new Cand(x.Page, x.Preview, x.Vec, cos, lex, boo, fin);
//        })
//        .OrderByDescending(c => c.Final)
//        .Take(Math.Max(K * 4, 12))
//        .ToList();

//        // 2) MMR for diversity
//        var picked = MMR(cands, K, lambda: 0.7f);

//        // 3) Dedupe-by-page ONLY for non-list queries.
//        if (!listy)
//        {
//            picked = picked
//                .GroupBy(c => c.Page)
//                .Select(g => g.First())
//                .ToList();
//        }

//        // 4) return lightweight tops
//        return picked.Select(c => new TopChunk(c.Page, c.Preview, c.Final)).ToList();
//    }


//    // ----- internals -----
//    private record Cand(int Page, string Preview, ReadOnlyMemory<float> Vec, float Cos, float Lex, float Boost, float Final);

//    private static List<Cand> MMR(List<Cand> cands, int K, float lambda)
//    {
//        var chosen = new List<Cand>();
//        var remaining = new List<Cand>(cands);

//        while (chosen.Count < K && remaining.Count > 0)
//        {
//            Cand best = null;
//            float bestScore = float.NegativeInfinity;

//            foreach (var c in remaining)
//            {
//                float div = 0f;
//                foreach (var s in chosen)
//                {
//                    var sim = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(c.Vec.Span, s.Vec.Span);
//                    if (sim > div) div = sim;
//                }
//                float score = lambda * c.Final - (1 - lambda) * div;
//                if (score > bestScore) { bestScore = score; best = c; }
//            }

//            chosen.Add(best);
//            remaining.Remove(best);
//        }

//        return chosen;
//    }
//}





