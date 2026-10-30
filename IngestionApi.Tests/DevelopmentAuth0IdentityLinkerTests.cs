using Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IngestionApi.Tests;

public sealed class DevelopmentAuth0IdentityLinkerTests
{
    private const string Subject = "auth0|disposable-link-test";

    [Fact]
    public async Task Linking_is_repeatable_and_cleanup_preserves_unrelated_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Users.CreateAsync(new NewUser(
            "existing-user",
            "existing@example.test",
            "existing-hash",
            "Existing User",
            false,
            DateTime.UtcNow));
        await database.AddSessionAsync("existing-session", "existing-user");

        var first = await database.Linker.LinkAsync(
            DevelopmentEnvironment(),
            EnabledOptions());
        var duplicate = await database.Linker.LinkAsync(
            DevelopmentEnvironment(),
            EnabledOptions());

        Assert.Equal(DevelopmentAuth0LinkStatus.CreatedAndLinked, first.Status);
        Assert.Equal(DevelopmentAuth0LinkStatus.AlreadyLinked, duplicate.Status);
        Assert.Equal(first.LocalUserId, duplicate.LocalUserId);
        Assert.Equal(first.LocalUserId, await database.Identities.ResolveLocalUserIdAsync("auth0", Subject));
        var disposable = await database.Users.GetProfileByIdAsync(first.LocalUserId);
        Assert.NotNull(disposable);
        Assert.False(disposable.IsSuperUser);
        Assert.Equal(1, await database.CountAsync("UserIdentities"));

        var cleanup = await database.Linker.CleanupAsync(
            DevelopmentEnvironment(),
            EnabledOptions());
        var repeatedCleanup = await database.Linker.CleanupAsync(
            DevelopmentEnvironment(),
            EnabledOptions());

        Assert.Equal(DevelopmentAuth0CleanupStatus.Removed, cleanup.Status);
        Assert.Equal(DevelopmentAuth0CleanupStatus.NothingToRemove, repeatedCleanup.Status);
        Assert.Null(await database.Users.GetProfileByIdAsync(first.LocalUserId));
        Assert.Null(await database.Identities.ResolveLocalUserIdAsync("auth0", Subject));
        Assert.NotNull(await database.Users.GetProfileByIdAsync("existing-user"));
        Assert.Equal(1, await database.CountAsync("Sessions"));
    }

    [Fact]
    public async Task Existing_subject_mapping_to_another_user_is_a_conflict()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Users.CreateAsync(new NewUser(
            "different-local-user",
            "different@example.test",
            "existing-hash",
            "Different User",
            false,
            DateTime.UtcNow));
        Assert.Equal(
            CreateUserIdentityResult.Created,
            await database.Identities.CreateAsync(
                "auth0",
                Subject,
                "different-local-user",
                DateTime.UtcNow));

        var result = await database.Linker.LinkAsync(
            DevelopmentEnvironment(),
            EnabledOptions());
        var cleanup = await database.Linker.CleanupAsync(
            DevelopmentEnvironment(),
            EnabledOptions());

        Assert.Equal(DevelopmentAuth0LinkStatus.Conflict, result.Status);
        Assert.Equal(DevelopmentAuth0CleanupStatus.RefusedMappingConflict, cleanup.Status);
        Assert.Equal("different-local-user", await database.Identities.ResolveLocalUserIdAsync("auth0", Subject));
        Assert.Null(await database.Users.GetProfileByIdAsync(
            DevelopmentAuth0IdentityLinker.GetDisposableLocalUserId(Subject)));
    }

    [Fact]
    public async Task Linking_is_rejected_outside_development_or_without_admin_flag()
    {
        await using var database = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Linker.LinkAsync(
            Environment(Environments.Production),
            EnabledOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Linker.LinkAsync(
            DevelopmentEnvironment(),
            new DevelopmentAuth0LinkOptions(false, Subject)));
        var postgresLinker = new DevelopmentAuth0IdentityLinker(
            new DatabaseOptions("postgres", "Host=not-used", null),
            database.Users,
            database.Identities);
        await Assert.ThrowsAsync<InvalidOperationException>(() => postgresLinker.LinkAsync(
            DevelopmentEnvironment(),
            EnabledOptions()));

        Assert.Equal(0, await database.CountAsync("Users"));
        Assert.Equal(0, await database.CountAsync("UserIdentities"));
    }

    [Fact]
    public async Task Cleanup_refuses_to_remove_user_with_related_application_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var link = await database.Linker.LinkAsync(
            DevelopmentEnvironment(),
            EnabledOptions());
        await database.AddSessionAsync("disposable-session", link.LocalUserId);

        var cleanup = await database.Linker.CleanupAsync(
            DevelopmentEnvironment(),
            EnabledOptions());

        Assert.Equal(DevelopmentAuth0CleanupStatus.RefusedRelatedData, cleanup.Status);
        Assert.NotNull(await database.Users.GetProfileByIdAsync(link.LocalUserId));
        Assert.Equal(link.LocalUserId, await database.Identities.ResolveLocalUserIdAsync("auth0", Subject));
        Assert.Equal(1, await database.CountAsync("Sessions"));
    }

    private static DevelopmentAuth0LinkOptions EnabledOptions() => new(true, Subject);
    private static IHostEnvironment DevelopmentEnvironment() => Environment(Environments.Development);
    private static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CasePilot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path;
        private readonly string _connectionString;

        private TestDatabase(string path)
        {
            _path = path;
            _connectionString = $"Data Source={path}";
            var options = new DatabaseOptions("sqlite", _connectionString, path);
            Users = new TestUserRepository(options);
            Identities = new UserIdentityRepository(options);
            Linker = new DevelopmentAuth0IdentityLinker(options, Users, Identities);
        }

        public TestUserRepository Users { get; }
        public UserIdentityRepository Identities { get; }
        public DevelopmentAuth0IdentityLinker Linker { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"casepilot-auth0-link-{Guid.NewGuid():N}.db");
            var database = new TestDatabase(path);
            await using var connection = new SqliteConnection(database._connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE Users (Id TEXT PRIMARY KEY, Email TEXT NOT NULL UNIQUE, PasswordHash TEXT NOT NULL, CreatedAt TEXT NOT NULL, FullName TEXT NULL, IsSuperUser INTEGER NOT NULL DEFAULT 0);
CREATE TABLE UserIdentities (Provider TEXT NOT NULL, ProviderSubject TEXT NOT NULL, UserId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (Provider, ProviderSubject), FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE RESTRICT);
CREATE TABLE Uploads (UploadId TEXT PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE Sessions (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE Notes (Id INTEGER PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE Classes (Id TEXT PRIMARY KEY, InstructorId TEXT NOT NULL);
CREATE TABLE ClassStudents (ClassId TEXT NOT NULL, StudentId TEXT NOT NULL);
CREATE TABLE TutorSessions (SessionId TEXT PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE TutorAnswers (Id INTEGER PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE TutorHelpEvents (Id INTEGER PRIMARY KEY, UserId TEXT NOT NULL);
CREATE TABLE IndexJobs (UploadId TEXT PRIMARY KEY, RequestedBy TEXT NULL);";
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async Task AddSessionAsync(string sessionId, string userId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Sessions (Id, UserId) VALUES (@id, @userId);";
            command.Parameters.AddWithValue("@id", sessionId);
            command.Parameters.AddWithValue("@userId", userId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> CountAsync(string table)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "Users", "UserIdentities", "Sessions" };
            if (!allowed.Contains(table)) throw new ArgumentOutOfRangeException(nameof(table));
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_path)) File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestUserRepository(DatabaseOptions options) : IUserRepository
    {
        public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        {
            await using var connection = options.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM Users WHERE Email = @email LIMIT 1;";
            command.AddWithValue("@email", email);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }

        public async Task CreateAsync(NewUser user, CancellationToken cancellationToken = default)
        {
            await using var connection = options.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO Users (Id, Email, PasswordHash, CreatedAt, FullName, IsSuperUser)
VALUES (@id, @email, @passwordHash, @createdAt, @fullName, @isSuperUser);";
            command.AddWithValue("@id", user.Id);
            command.AddWithValue("@email", user.Email);
            command.AddWithValue("@passwordHash", user.PasswordHash);
            command.AddWithValue("@createdAt", user.CreatedAt.ToString("o"));
            command.AddWithValue("@fullName", user.FullName);
            command.AddWithValue("@isSuperUser", user.IsSuperUser ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public Task<UserCredentialRecord?> GetCredentialsByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<UserProfileRecord?> GetProfileByIdAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            await using var connection = options.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Email, COALESCE(FullName,''), IsSuperUser FROM Users WHERE Id = @id LIMIT 1;";
            command.AddWithValue("@id", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new UserProfileRecord(userId, reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0)
                : null;
        }
    }
}
