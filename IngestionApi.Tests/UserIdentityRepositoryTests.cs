using Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IngestionApi.Tests;

public sealed class UserIdentityRepositoryTests
{
    [Fact]
    public async Task Create_and_resolve_identity_preserves_existing_local_user_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        var localUserId = await database.AddUserAsync();

        var result = await database.Repository.CreateAsync(
            "Auth0",
            "auth0|student-123",
            localUserId,
            DateTime.UtcNow);

        Assert.Equal(CreateUserIdentityResult.Created, result);
        Assert.Equal(
            localUserId,
            await database.Repository.ResolveLocalUserIdAsync("auth0", "auth0|student-123"));
    }

    [Fact]
    public async Task Resolve_returns_null_when_identity_does_not_exist()
    {
        await using var database = await TestDatabase.CreateAsync();

        var localUserId = await database.Repository.ResolveLocalUserIdAsync("auth0", "missing-subject");

        Assert.Null(localUserId);
    }

    [Fact]
    public async Task Duplicate_and_conflicting_subject_mappings_are_rejected()
    {
        await using var database = await TestDatabase.CreateAsync();
        var originalUserId = await database.AddUserAsync();
        var otherUserId = await database.AddUserAsync();
        var createdAt = DateTime.UtcNow;

        Assert.Equal(
            CreateUserIdentityResult.Created,
            await database.Repository.CreateAsync("auth0", "shared-subject", originalUserId, createdAt));
        Assert.Equal(
            CreateUserIdentityResult.AlreadyExists,
            await database.Repository.CreateAsync("auth0", "shared-subject", originalUserId, createdAt));
        Assert.Equal(
            CreateUserIdentityResult.Conflict,
            await database.Repository.CreateAsync("auth0", "shared-subject", otherUserId, createdAt));

        Assert.Equal(originalUserId,
            await database.Repository.ResolveLocalUserIdAsync("auth0", "shared-subject"));
        Assert.Equal(1, await database.CountIdentitiesAsync());
    }

    [Fact]
    public async Task Create_rejects_nonexistent_local_user()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Repository.CreateAsync(
            "auth0",
            "auth0|orphaned",
            "nonexistent-user",
            DateTime.UtcNow);

        Assert.Equal(CreateUserIdentityResult.UserNotFound, result);
        Assert.Null(await database.Repository.ResolveLocalUserIdAsync("auth0", "auth0|orphaned"));
        Assert.Equal(0, await database.CountIdentitiesAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        private TestDatabase(string databasePath)
        {
            _databasePath = databasePath;
            _connectionString = $"Data Source={databasePath}";
            Repository = new UserIdentityRepository(new DatabaseOptions("sqlite", _connectionString, databasePath));
        }

        public UserIdentityRepository Repository { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"casepilot-identities-{Guid.NewGuid():N}.db");
            var database = new TestDatabase(databasePath);

            await using var connection = new SqliteConnection(database._connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA foreign_keys = ON;
CREATE TABLE Users (
  Id TEXT PRIMARY KEY,
  Email TEXT NOT NULL UNIQUE,
  PasswordHash TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  FullName TEXT NULL,
  IsSuperUser INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE UserIdentities (
  Provider TEXT NOT NULL,
  ProviderSubject TEXT NOT NULL,
  UserId TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  PRIMARY KEY (Provider, ProviderSubject),
  FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE RESTRICT
);
CREATE INDEX IX_UserIdentities_UserId ON UserIdentities (UserId);";
            await command.ExecuteNonQueryAsync();

            return database;
        }

        public async Task<string> AddUserAsync()
        {
            var userId = Guid.NewGuid().ToString("N");
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO Users (Id, Email, PasswordHash, CreatedAt, FullName, IsSuperUser)
VALUES (@id, @email, 'test-only', @createdAt, 'Test User', 0);";
            command.Parameters.AddWithValue("@id", userId);
            command.Parameters.AddWithValue("@email", $"{userId}@example.test");
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
            await command.ExecuteNonQueryAsync();
            return userId;
        }

        public async Task<long> CountIdentitiesAsync()
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM UserIdentities;";
            return (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            return ValueTask.CompletedTask;
        }
    }
}
