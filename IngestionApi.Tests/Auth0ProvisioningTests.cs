using Api.Extensions;
using Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IngestionApi.Tests;

public sealed class Auth0ProvisioningTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"casepilot-provision-{Guid.NewGuid():N}.db");
    private readonly DatabaseOptions _database;

    public Auth0ProvisioningTests()
    {
        _database = new DatabaseOptions("sqlite", $"Data Source={_path};Pooling=False", _path);
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE Users (
  Id TEXT PRIMARY KEY, Email TEXT NOT NULL UNIQUE, PasswordHash TEXT NOT NULL,
  CreatedAt TEXT NOT NULL, FullName TEXT NULL, IsSuperUser INTEGER NOT NULL DEFAULT 0,
  IsDisabled INTEGER NOT NULL DEFAULT 0);
CREATE TABLE UserIdentities (
  Provider TEXT NOT NULL, ProviderSubject TEXT NOT NULL, UserId TEXT NOT NULL,
  CreatedAt TEXT NOT NULL, PRIMARY KEY (Provider, ProviderSubject),
  FOREIGN KEY (UserId) REFERENCES Users(Id));";
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task Creates_only_a_student_and_is_idempotent_for_the_same_subject()
    {
        var service = new Auth0ProvisioningService(_database);
        var first = await service.CreateStudentAsync(
            "auth0|new-student", "new.student@example.test", "New Student", default);
        var second = await service.CreateStudentAsync(
            "auth0|new-student", "new.student@example.test", "New Student", default);

        Assert.Equal(Auth0ProvisioningResult.Created, first.Result);
        Assert.Equal(Auth0ProvisioningResult.AlreadyLinked, second.Result);
        Assert.Equal(first.UserId, second.UserId);

        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsSuperUser, IsDisabled, PasswordHash FROM Users WHERE Id = @id";
        command.Parameters.AddWithValue("@id", first.UserId);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.True(BCrypt.Net.BCrypt.Verify(
            Convert.ToBase64String(new byte[48]), reader.GetString(2)) is false);
    }

    [Fact]
    public async Task Existing_email_requires_verified_account_linking_and_is_not_auto_linked()
    {
        await InsertExistingUserAsync("existing-user", "existing@example.test");
        var service = new Auth0ProvisioningService(_database);

        var result = await service.CreateStudentAsync(
            "auth0|different-subject", "EXISTING@example.test", "Impostor", default);

        Assert.Equal(Auth0ProvisioningResult.ExistingAccountRequiresLinking, result.Result);
        Assert.Null(result.UserId);
        Assert.Equal(0, await CountAsync("UserIdentities"));
        Assert.Equal(1, await CountAsync("Users"));
    }

    [Fact]
    public async Task Provider_subject_conflict_does_not_create_another_user()
    {
        var service = new Auth0ProvisioningService(_database);
        var first = await service.CreateStudentAsync(
            "auth0|same", "first@example.test", "First", default);
        var second = await service.CreateStudentAsync(
            "auth0|same", "second@example.test", "Second", default);

        Assert.Equal(Auth0ProvisioningResult.Created, first.Result);
        Assert.Equal(Auth0ProvisioningResult.AlreadyLinked, second.Result);
        Assert.Equal(1, await CountAsync("Users"));
        Assert.Equal(1, await CountAsync("UserIdentities"));
    }

    private async Task InsertExistingUserAsync(string id, string email)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO Users (Id, Email, PasswordHash, CreatedAt, FullName, IsSuperUser, IsDisabled)
VALUES (@id, @email, 'legacy-hash', @created, 'Existing', 0, 0);";
        command.AddWithValue("@id", id);
        command.AddWithValue("@email", email);
        command.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
