using Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IngestionApi.Tests;

public sealed class Auth0MigrationReadinessReporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"casepilot-auth0-report-{Guid.NewGuid():N}.db");
    private readonly DatabaseOptions _database;

    public Auth0MigrationReadinessReporterTests()
    {
        _database = new DatabaseOptions("sqlite", $"Data Source={_path};Pooling=False", _path);
        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE Users (Id TEXT PRIMARY KEY, Email TEXT, PasswordHash TEXT, IsSuperUser INTEGER, IsDisabled INTEGER);
CREATE TABLE UserIdentities (Provider TEXT, ProviderSubject TEXT, UserId TEXT);
INSERT INTO Users VALUES ('student', 'student@example.test', '$2b$10$aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 0, 0);
INSERT INTO Users VALUES ('instructor', 'instructor@example.test', 'legacy-hash', 1, 1);
INSERT INTO UserIdentities VALUES ('auth0', 'auth0|student', 'student');";
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task Reports_only_aggregate_readiness_and_does_not_modify_data()
    {
        var report = await Auth0MigrationReadinessReporter.RunAsync(_database);
        using var output = new StringWriter();
        Auth0MigrationReadinessReporter.WriteSafeSummary(report, output);

        Assert.Equal(new Auth0MigrationReadinessReport(2, 1, 1, 1, 1, 1, 1), report);
        Assert.DoesNotContain("student@example", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth0|student", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await CountUsersAsync());
    }

    private async Task<long> CountUsersAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
