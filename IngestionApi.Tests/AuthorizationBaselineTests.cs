using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Api.Endpoints;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IngestionApi.Tests;

public sealed class AuthorizationBaselineTests : IClassFixture<AuthorizationTestApplication>
{
    private readonly AuthorizationTestApplication _application;

    public AuthorizationBaselineTests(AuthorizationTestApplication application)
    {
        _application = application;
    }

    [Fact]
    public async Task Student_CannotAccessInstructorOnlyOperation()
    {
        using var response = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/classes/mine",
            AuthorizationTestApplication.AssignedStudentEmail,
            AuthorizationTestApplication.AssignedStudentPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Instructor_CanAccessOwnClass_ButNotAnotherInstructorsClass()
    {
        var token = await _application.LoginAsync(
            AuthorizationTestApplication.FirstInstructorEmail,
            AuthorizationTestApplication.FirstInstructorPassword);

        using var listResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/classes/mine",
            token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listedClassIds = listBody.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains(AuthorizationTestApplication.FirstInstructorClassId, listedClassIds);
        Assert.DoesNotContain(AuthorizationTestApplication.SecondInstructorClassId, listedClassIds);

        using var ownClassResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/classes/{AuthorizationTestApplication.FirstInstructorClassId}/details",
            token);
        Assert.Equal(HttpStatusCode.OK, ownClassResponse.StatusCode);

        using var otherClassResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/classes/{AuthorizationTestApplication.SecondInstructorClassId}/details",
            token);
        Assert.Equal(HttpStatusCode.NotFound, otherClassResponse.StatusCode);
    }

    [Fact]
    public async Task UploadOwner_CanAccessDocument()
    {
        using var response = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            AuthorizationTestApplication.DocumentPath,
            AuthorizationTestApplication.UploadOwnerEmail,
            AuthorizationTestApplication.UploadOwnerPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StudentAssignedThroughClass_CanAccessDocument()
    {
        using var response = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            AuthorizationTestApplication.DocumentPath,
            AuthorizationTestApplication.AssignedStudentEmail,
            AuthorizationTestApplication.AssignedStudentPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnrelatedAuthenticatedUser_CannotDiscoverDocument()
    {
        using var response = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            AuthorizationTestApplication.DocumentPath,
            AuthorizationTestApplication.UnrelatedUserEmail,
            AuthorizationTestApplication.UnrelatedUserPassword);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUsers_ReceiveOnlyTheirOwnUploadsAndSessions()
    {
        var ownerToken = await _application.LoginAsync(
            AuthorizationTestApplication.UploadOwnerEmail,
            AuthorizationTestApplication.UploadOwnerPassword);

        using var ownerUploadsResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/uploads/mine",
            ownerToken);
        Assert.Equal(HttpStatusCode.OK, ownerUploadsResponse.StatusCode);
        using var ownerUploadsBody = JsonDocument.Parse(await ownerUploadsResponse.Content.ReadAsStringAsync());
        var ownerUploadIds = ownerUploadsBody.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("uploadId").GetString())
            .ToArray();
        Assert.Equal(new[] { AuthorizationTestApplication.UploadIdText }, ownerUploadIds);

        using var ownerSessionsResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/sessions/mine",
            ownerToken);
        Assert.Equal(HttpStatusCode.OK, ownerSessionsResponse.StatusCode);
        using var ownerSessionsBody = JsonDocument.Parse(await ownerSessionsResponse.Content.ReadAsStringAsync());
        var ownerSessionIds = ownerSessionsBody.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("sessionId").GetString())
            .ToArray();
        Assert.Equal(new[] { AuthorizationTestApplication.OwnerSessionId }, ownerSessionIds);

        var unrelatedToken = await _application.LoginAsync(
            AuthorizationTestApplication.UnrelatedUserEmail,
            AuthorizationTestApplication.UnrelatedUserPassword);

        using var unrelatedUploadsResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/uploads/mine",
            unrelatedToken);
        Assert.Equal(HttpStatusCode.OK, unrelatedUploadsResponse.StatusCode);
        using var unrelatedUploadsBody = JsonDocument.Parse(await unrelatedUploadsResponse.Content.ReadAsStringAsync());
        Assert.Empty(unrelatedUploadsBody.RootElement.GetProperty("items").EnumerateArray());

        using var unrelatedSessionsResponse = await _application.SendAuthenticatedAsync(
            HttpMethod.Get,
            "/sessions/mine",
            unrelatedToken);
        Assert.Equal(HttpStatusCode.OK, unrelatedSessionsResponse.StatusCode);
        using var unrelatedSessionsBody = JsonDocument.Parse(await unrelatedSessionsResponse.Content.ReadAsStringAsync());
        var unrelatedSessionIds = unrelatedSessionsBody.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("sessionId").GetString())
            .ToArray();
        Assert.Equal(new[] { AuthorizationTestApplication.UnrelatedSessionId }, unrelatedSessionIds);
    }

    [Theory]
    [InlineData("/me")]
    [InlineData("/classes/enrolled")]
    [InlineData("/uploads/mine")]
    [InlineData("/sessions/mine")]
    public async Task AnonymousUser_CannotAccessProtectedEndpoint(string path)
    {
        using var response = await _application.Client.GetAsync(path);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403 for anonymous GET {path}, but received {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task AnonymousUser_CannotAccessDocument()
    {
        using var response = await _application.Client.GetAsync(AuthorizationTestApplication.DocumentPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class AuthorizationTestApplication : IAsyncLifetime
{
    public const string FirstInstructorClassId = "class-owned-by-instructor-one";
    public const string SecondInstructorClassId = "class-owned-by-instructor-two";
    public const string OwnerSessionId = "owner-session-id";
    public const string UnrelatedSessionId = "unrelated-session-id";

    public const string FirstInstructorEmail = "instructor-one@example.test";
    public const string FirstInstructorPassword = "instructor-one-password";
    public const string AssignedStudentEmail = "assigned-student@example.test";
    public const string AssignedStudentPassword = "assigned-student-password";
    public const string UploadOwnerEmail = "upload-owner@example.test";
    public const string UploadOwnerPassword = "upload-owner-password";
    public const string UnrelatedUserEmail = "unrelated-user@example.test";
    public const string UnrelatedUserPassword = "unrelated-user-password";

    private const string FirstInstructorId = "instructor-one-id";
    private const string SecondInstructorId = "instructor-two-id";
    private const string AssignedStudentId = "assigned-student-id";
    private const string UploadOwnerId = "upload-owner-id";
    private const string UnrelatedUserId = "unrelated-user-id";
    private const string SecondInstructorEmail = "instructor-two@example.test";
    private const string JwtSecret = "authorization-tests-only-secret-at-least-32-characters";
    private static readonly Guid UploadId = Guid.Parse("f2e46d1c-808f-4c91-adc9-4304a1786f41");

    private readonly StringBuilder _serverOutput = new();
    private string? _testRoot;
    private string? _databasePath;
    private Process? _serverProcess;

    public static string DocumentPath => $"/uploads/{UploadId}.pdf";
    public static string UploadIdText => UploadId.ToString();
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"casepilot-authorization-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _databasePath = Path.Combine(_testRoot, "authorization-tests.db");

        var port = ReserveLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        StartServer(baseAddress);
        Client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };

        await WaitForServerAsync();
        await SeedDatabaseAsync();
        Directory.CreateDirectory(Path.Combine(_testRoot, "uploads"));
        await File.WriteAllBytesAsync(
            Path.Combine(_testRoot, "uploads", $"{UploadId}.pdf"),
            "%PDF-1.4\n%%EOF\n"u8.ToArray());
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (_serverProcess is { HasExited: false })
        {
            _serverProcess.Kill(entireProcessTree: true);
            await _serverProcess.WaitForExitAsync();
        }
        _serverProcess?.Dispose();
        SqliteConnection.ClearAllPools();

        if (_testRoot is not null && Directory.Exists(_testRoot))
        {
            var resolvedRoot = Path.GetFullPath(_testRoot);
            var resolvedTemp = Path.GetFullPath(Path.GetTempPath());
            if (!resolvedRoot.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolvedRoot).StartsWith("casepilot-authorization-tests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to delete unexpected test directory '{resolvedRoot}'.");
            }

            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        using var response = await Client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Login response did not contain a JWT.");
    }

    public async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string path,
        string email,
        string password)
    {
        var token = await LoginAsync(email, password);
        return await SendAuthenticatedAsync(method, path, token);
    }

    public Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    private void StartServer(Uri baseAddress)
    {
        var applicationAssembly = typeof(AuthEndpoints).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _testRoot!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(applicationAssembly);

        SetEnvironment(startInfo, "ASPNETCORE_URLS", baseAddress.ToString().TrimEnd('/'));
        SetEnvironment(startInfo, "ASPNETCORE_ENVIRONMENT", "Testing");
        SetEnvironment(startInfo, "DATABASE_PROVIDER", "sqlite");
        SetEnvironment(startInfo, "SQLITE_CONNECTION_STRING", $"Data Source={_databasePath};Cache=Shared");
        SetEnvironment(startInfo, "DOCUMENT_STORAGE_PROVIDER", "local");
        SetEnvironment(startInfo, "RUN_BACKGROUND_WORKER", "false");
        SetEnvironment(startInfo, "OPENAI_API_KEY", "test-only-openai-key");
        SetEnvironment(startInfo, "JWT_SECRET", JwtSecret);
        SetEnvironment(startInfo, "JWT_ISSUER", "CasePilot.AuthorizationTests");
        SetEnvironment(startInfo, "JWT_AUDIENCE", "CasePilot.AuthorizationTests.Client");
        SetEnvironment(startInfo, "ENABLE_DEBUG_ENDPOINTS", "false");
        SetEnvironment(startInfo, "TRUST_FORWARDED_HEADERS", "false");

        foreach (var variable in new[]
                 {
                     "POSTGRES_CONNECTION_STRING",
                     "AZURE_STORAGE_CONNECTION_STRING",
                     "AZURE_STORAGE_CONTAINER",
                     "REDIS_CONNECTION_STRING",
                     "OPENROUTER_API_KEY",
                     "OPENROUTER_BASE_URL",
                     "OTEL_EXPORTER_OTLP_ENDPOINT",
                     "APPLICATIONINSIGHTS_CONNECTION_STRING",
                     "INSTRUCTOR_INVITE_CODES"
                 })
        {
            startInfo.Environment.Remove(variable);
        }

        _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _serverProcess.OutputDataReceived += (_, args) => AppendServerOutput(args.Data);
        _serverProcess.ErrorDataReceived += (_, args) => AppendServerOutput(args.Data);
        if (!_serverProcess.Start())
        {
            throw new InvalidOperationException("Failed to start the isolated CasePilot server.");
        }
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
    }

    private async Task WaitForServerAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_serverProcess?.HasExited == true)
            {
                throw new InvalidOperationException(
                    $"The isolated CasePilot server exited early.{Environment.NewLine}{_serverOutput}");
            }

            try
            {
                using var response = await Client.GetAsync("/ping");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel has not started listening yet.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"The isolated CasePilot server did not start within 30 seconds.{Environment.NewLine}{_serverOutput}");
    }

    private async Task SeedDatabaseAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await InsertUserAsync(connection, transaction, FirstInstructorId, FirstInstructorEmail, FirstInstructorPassword, isInstructor: true);
        await InsertUserAsync(connection, transaction, SecondInstructorId, SecondInstructorEmail, "instructor-two-password", isInstructor: true);
        await InsertUserAsync(connection, transaction, AssignedStudentId, AssignedStudentEmail, AssignedStudentPassword, isInstructor: false);
        await InsertUserAsync(connection, transaction, UploadOwnerId, UploadOwnerEmail, UploadOwnerPassword, isInstructor: false);
        await InsertUserAsync(connection, transaction, UnrelatedUserId, UnrelatedUserEmail, UnrelatedUserPassword, isInstructor: false);

        await ExecuteAsync(connection, transaction, @"
INSERT INTO Classes (Id, InstructorId, Name, Description, JoinCode, CreatedAt)
VALUES
  (@firstClassId, @firstInstructorId, 'Instructor One Class', 'Owned class', 'OWNED001', @createdAt),
  (@secondClassId, @secondInstructorId, 'Instructor Two Class', 'Other class', 'OTHER001', @createdAt);",
            ("@firstClassId", FirstInstructorClassId),
            ("@firstInstructorId", FirstInstructorId),
            ("@secondClassId", SecondInstructorClassId),
            ("@secondInstructorId", SecondInstructorId),
            ("@createdAt", DateTime.UtcNow.ToString("O")));

        await ExecuteAsync(connection, transaction, @"
INSERT INTO Uploads (UploadId, UserId, FilePath, Name, OriginalFileName, CreatedAt)
VALUES (@uploadId, @ownerId, @filePath, 'Authorization Test Document', 'authorization-test.pdf', @createdAt);",
            ("@uploadId", UploadId.ToString()),
            ("@ownerId", UploadOwnerId),
            ("@filePath", Path.Combine(_testRoot!, "uploads", $"{UploadId}.pdf")),
            ("@createdAt", DateTime.UtcNow.ToString("O")));

        await ExecuteAsync(connection, transaction, @"
INSERT INTO ClassStudents (ClassId, StudentId, AddedAt)
VALUES (@classId, @studentId, @addedAt);
INSERT INTO ClassCases (ClassId, UploadId, AssignedAt)
VALUES (@classId, @uploadId, @assignedAt);",
            ("@classId", FirstInstructorClassId),
            ("@studentId", AssignedStudentId),
            ("@uploadId", UploadId.ToString()),
            ("@addedAt", DateTime.UtcNow.ToString("O")),
            ("@assignedAt", DateTime.UtcNow.ToString("O")));

        await ExecuteAsync(connection, transaction, @"
INSERT INTO Sessions (Id, UserId, UploadId, ClassId, CreatedAt)
VALUES
  (@ownerSessionId, @ownerId, @uploadId, NULL, @createdAt),
  (@unrelatedSessionId, @unrelatedUserId, NULL, NULL, @createdAt);",
            ("@ownerSessionId", OwnerSessionId),
            ("@ownerId", UploadOwnerId),
            ("@uploadId", UploadId.ToString()),
            ("@unrelatedSessionId", UnrelatedSessionId),
            ("@unrelatedUserId", UnrelatedUserId),
            ("@createdAt", DateTime.UtcNow.ToString("O")));

        await transaction.CommitAsync();
    }

    private static Task InsertUserAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string id,
        string email,
        string password,
        bool isInstructor) =>
        ExecuteAsync(connection, transaction, @"
INSERT INTO Users (Id, Email, PasswordHash, CreatedAt, FullName, IsSuperUser)
VALUES (@id, @email, @passwordHash, @createdAt, @fullName, @isSuperUser);",
            ("@id", id),
            ("@email", email),
            ("@passwordHash", BCrypt.Net.BCrypt.HashPassword(password)),
            ("@createdAt", DateTime.UtcNow.ToString("O")),
            ("@fullName", email.Split('@')[0]),
            ("@isSuperUser", isInstructor ? 1 : 0));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        await command.ExecuteNonQueryAsync();
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void SetEnvironment(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.Environment[name] = value;
    }

    private void AppendServerOutput(string? line)
    {
        if (line is null) return;
        lock (_serverOutput)
        {
            _serverOutput.AppendLine(line);
        }
    }
}
