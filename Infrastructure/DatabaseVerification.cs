using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Api.Infrastructure;

public static class DatabaseVerification
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[DB VERIFY] Creating repository test data...");
        var users = services.GetRequiredService<IUserRepository>();
        var classes = services.GetRequiredService<IClassRepository>();
        var sessions = services.GetRequiredService<ISessionRepository>();
        var messages = services.GetRequiredService<IMessageRepository>();
        var jobs = services.GetRequiredService<IndexJobStore>();
        var storage = services.GetRequiredService<IDocumentStorage>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var suffix = Guid.NewGuid().ToString("N");
        var instructorId = $"verify-instructor-{suffix}";
        var studentId = $"verify-student-{suffix}";
        var now = DateTime.UtcNow;

        await users.CreateAsync(new NewUser(
            instructorId,
            $"instructor-{suffix}@example.test",
            "verification-only",
            "Verification Instructor",
            true,
            now), cancellationToken);
        await users.CreateAsync(new NewUser(
            studentId,
            $"student-{suffix}@example.test",
            "verification-only",
            "verification student",
            false,
            now), cancellationToken);

        var createdClass = await classes.CreateAsync(
            instructorId,
            $"Database verification {suffix}",
            "Temporary integration check",
            cancellationToken);

        var firstJoin = await classes.JoinByCodeAsync(studentId, createdClass.JoinCode, cancellationToken);
        var duplicateJoin = await classes.JoinByCodeAsync(studentId, createdClass.JoinCode, cancellationToken);
        Ensure(firstJoin.ClassFound && duplicateJoin.ClassFound, "Class join failed.");

        var instructorClasses = await classes.ListMineAsync(instructorId, cancellationToken);
        Ensure(instructorClasses.Count == 1, "Instructor class query returned an unexpected row count.");
        Ensure(instructorClasses[0].StudentCount == 1, "Idempotent class join or COUNT conversion failed.");

        var students = await classes.ListStudentsAsync(createdClass.Id, instructorId, cancellationToken);
        Ensure(students is { Count: 1 } && students[0].StudentId == studentId,
            "Cross-provider student ordering query failed.");

        Console.WriteLine("[DB VERIFY] Verifying sessions, aggregates, and identity values...");
        var sessionId = $"verify-session-{suffix}";
        await sessions.CreateAsync(sessionId, studentId, null, now, createdClass.Id, cancellationToken);
        await messages.SaveAsync(sessionId, "user", "Database verification message", null, null, cancellationToken);
        var note = await sessions.AddNoteAsync(sessionId, studentId, "Database verification note", cancellationToken);
        Ensure(note is not null, "Identity RETURNING failed while adding a note.");

        var sessionPage = await sessions.ListMineAsync(studentId, 1, 20, cancellationToken: cancellationToken);
        Ensure(sessionPage.Items.Count == 1, "Session listing returned an unexpected row count.");
        Ensure(sessionPage.Items[0].MessageCount == 1 && sessionPage.Items[0].NotesCount == 1,
            "PostgreSQL aggregate conversion failed.");

        Console.WriteLine("[DB VERIFY] Verifying concurrent worker claims and lease ownership...");
        var uploadA = Guid.NewGuid();
        var uploadB = Guid.NewGuid();
        await jobs.EnqueueAsync(uploadA, instructorId, cancellationToken);
        await jobs.EnqueueAsync(uploadB, instructorId, cancellationToken);

        var claims = await Task.WhenAll(
            jobs.TryClaimNextAsync("verification-worker-a", TimeSpan.FromMinutes(30), cancellationToken),
            jobs.TryClaimNextAsync("verification-worker-b", TimeSpan.FromMinutes(30), cancellationToken));
        Ensure(claims.All(claim => claim is not null), "Concurrent workers did not each claim a job.");
        Ensure(claims.Select(claim => claim!.UploadId).Distinct().Count() == 2,
            "Concurrent workers claimed the same job.");

        var firstClaim = claims[0]!;
        var summary = new IndexBuildSummary(firstClaim.UploadId, 1, 1, Array.Empty<IndexBuildSample>());
        await jobs.MarkCompletedAsync(firstClaim.UploadId, summary, "wrong-worker", cancellationToken);
        Ensure((await jobs.GetAsync(firstClaim.UploadId, cancellationToken))?.Status == "running",
            "A worker without the lease completed another worker's job.");

        await jobs.MarkCompletedAsync(firstClaim.UploadId, summary, firstClaim.WorkerId, cancellationToken);
        Ensure((await jobs.GetAsync(firstClaim.UploadId, cancellationToken))?.Status == "completed",
            "The owning worker could not complete its job.");

        Console.WriteLine("[DB VERIFY] Verifying shared document storage contract...");
        var artifactId = Guid.NewGuid();
        var pdfBytes = "%PDF-1.4 verification"u8.ToArray();
        await using (var pdfStream = new MemoryStream(pdfBytes))
        {
            var formFile = new FormFile(pdfStream, 0, pdfBytes.Length, "file", "verification.pdf");
            await storage.SavePdfAsync(artifactId, formFile, cancellationToken);
        }

        Ensure(await storage.PdfExistsAsync(artifactId, cancellationToken),
            "Saved PDF was not visible through shared storage.");
        var cachedPdfPath = await storage.GetPdfPathAsync(artifactId, cancellationToken);
        Ensure(cachedPdfPath is not null && File.Exists(cachedPdfPath),
            "Shared PDF could not be materialized for PDF processing.");
        if (storage is AzureBlobDocumentStorage)
        {
            File.Delete(cachedPdfPath!);
            var downloadedPdfPath = await storage.GetPdfPathAsync(artifactId, cancellationToken);
            Ensure(downloadedPdfPath is not null
                   && File.Exists(downloadedPdfPath)
                   && File.ReadAllBytes(downloadedPdfPath).SequenceEqual(pdfBytes),
                "A second instance could not download the shared PDF from Blob Storage.");
        }

        await storage.WriteJsonAsync(artifactId, DocumentArtifactSuffixes.Summary, new { verified = true }, cancellationToken);
        await storage.WriteJsonAsync(artifactId, DocumentArtifactSuffixes.Index, new[] { new { page = 1 } }, cancellationToken);
        await storage.WriteJsonAsync(artifactId, DocumentArtifactSuffixes.Layout, new { pages = 1 }, cancellationToken);
        await storage.WriteJsonAsync(artifactId, DocumentArtifactSuffixes.DocumentType, new { docType = "verification" }, cancellationToken);

        Ensure(!string.IsNullOrWhiteSpace(await storage.ReadTextAsync(
                artifactId,
                DocumentArtifactSuffixes.Index,
                cancellationToken)),
            "Shared index could not be read by another process boundary.");
        Ensure(await storage.ExistsAsync(artifactId, DocumentArtifactSuffixes.DocumentType, cancellationToken),
            "Document classification was not persisted.");

        var summaryFound = false;
        await foreach (var summaryFile in storage.EnumerateSummariesAsync(cancellationToken))
        {
            if (string.Equals(summaryFile.UploadId, artifactId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                summaryFound = true;
                break;
            }
        }
        Ensure(summaryFound, "Stored summary was missing from enumeration.");

        await storage.DeleteArtifactsAsync(artifactId, cancellationToken);
        Ensure(!await storage.PdfExistsAsync(artifactId, cancellationToken)
               && !await storage.ExistsAsync(artifactId, DocumentArtifactSuffixes.Index, cancellationToken)
               && !await storage.ExistsAsync(artifactId, DocumentArtifactSuffixes.DocumentType, cancellationToken),
            "Artifact deletion left shared files behind.");

        if (storage is AzureBlobDocumentStorage)
        {
            Console.WriteLine("[DB VERIFY] Verifying non-destructive local-to-Blob migration...");
            var migrationId = Guid.NewGuid();
            var migrationRoot = Path.Combine(Path.GetTempPath(), $"casepilot-migration-verify-{Guid.NewGuid():N}");
            var migrationUploads = Path.Combine(migrationRoot, "uploads");
            Directory.CreateDirectory(migrationUploads);
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(migrationUploads, $"{migrationId}.pdf"), pdfBytes, cancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(migrationUploads, $"docclass-{migrationId}.json"),
                    "{\"docType\":\"legacy-verification\"}",
                    cancellationToken);

                var migrationEnvironment = new VerificationHostEnvironment(migrationRoot);
                var migrationResult = await DocumentStorageMigrator.MigrateLocalToAzureAsync(
                    configuration,
                    migrationEnvironment,
                    cancellationToken);
                Ensure(migrationResult.PdfsCopied == 1 && migrationResult.JsonArtifactsCopied == 1,
                    "Existing local artifacts were not copied to Blob Storage.");
                Ensure(await storage.PdfExistsAsync(migrationId, cancellationToken)
                       && await storage.ExistsAsync(migrationId, DocumentArtifactSuffixes.DocumentType, cancellationToken),
                    "Migrated artifacts were not visible through shared storage.");
                Ensure(File.Exists(Path.Combine(migrationUploads, $"{migrationId}.pdf")),
                    "The non-destructive migration removed a local source artifact.");

                await storage.DeleteArtifactsAsync(migrationId, cancellationToken);
            }
            finally
            {
                Directory.Delete(migrationRoot, recursive: true);
            }
        }

        Console.WriteLine("[DB VERIFY] All checks passed.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Database verification failed: {message}");
    }

    private sealed class VerificationHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "StorageMigrationVerification";
        public string ApplicationName { get; set; } = "CasePilot.DatabaseVerification";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
