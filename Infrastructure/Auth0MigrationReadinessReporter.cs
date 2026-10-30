using System.Data.Common;

namespace Api.Infrastructure;

public sealed record Auth0MigrationReadinessReport(
    long Users,
    long LinkedAuth0Identities,
    long UnlinkedUsers,
    long DisabledUsers,
    long InstructorUsers,
    long BcryptHashes,
    long OtherHashes);

/// <summary>
/// Produces an aggregate, read-only migration report. It deliberately never
/// reads identity-provider credentials and never emits emails, IDs, or hashes.
/// </summary>
public static class Auth0MigrationReadinessReporter
{
    public static async Task<Auth0MigrationReadinessReport> RunAsync(
        DatabaseOptions database,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var users = await ScalarAsync(connection, "SELECT COUNT(*) FROM Users", cancellationToken);
        var linked = await ScalarAsync(connection,
            "SELECT COUNT(DISTINCT UserId) FROM UserIdentities WHERE Provider = 'auth0'",
            cancellationToken);
        var disabled = await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Users WHERE COALESCE(IsDisabled, 0) <> 0",
            cancellationToken);
        var instructors = await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Users WHERE COALESCE(IsSuperUser, 0) <> 0",
            cancellationToken);
        var bcrypt = await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Users WHERE PasswordHash LIKE '$2a$%' OR PasswordHash LIKE '$2b$%' OR PasswordHash LIKE '$2y$%'",
            cancellationToken);

        return new Auth0MigrationReadinessReport(
            users,
            linked,
            Math.Max(0, users - linked),
            disabled,
            instructors,
            bcrypt,
            Math.Max(0, users - bcrypt));
    }

    public static void WriteSafeSummary(Auth0MigrationReadinessReport report, TextWriter output)
    {
        output.WriteLine("[AUTH0 MIGRATION DRY RUN] No data was modified and no credentials were exported.");
        output.WriteLine($"users={report.Users} linkedAuth0={report.LinkedAuth0Identities} unlinked={report.UnlinkedUsers}");
        output.WriteLine($"disabled={report.DisabledUsers} instructorsRequiringApproval={report.InstructorUsers}");
        output.WriteLine($"bcryptHashes={report.BcryptHashes} hashesRequiringSeparateCompatibilityReview={report.OtherHashes}");
    }

    private static async Task<long> ScalarAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
