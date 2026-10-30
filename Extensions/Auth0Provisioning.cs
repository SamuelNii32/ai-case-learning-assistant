using System.Security.Cryptography;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Api.Extensions;

public sealed record Auth0ProvisioningSettings(
    bool StudentRegistrationEnabled,
    bool ExistingAccountLinkingEnabled)
{
    public static Auth0ProvisioningSettings Load(IConfiguration configuration) => new(
        ReadBool(configuration, "AUTH0_STUDENT_REGISTRATION_ENABLED", "Auth0:StudentRegistrationEnabled"),
        ReadBool(configuration, "AUTH0_EXISTING_ACCOUNT_LINKING_ENABLED", "Auth0:ExistingAccountLinkingEnabled"));

    private static bool ReadBool(IConfiguration configuration, string environmentName, string key)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName) ?? configuration[key] ?? "false";
        if (!bool.TryParse(raw, out var value))
            throw new InvalidOperationException($"{environmentName} must be 'true' or 'false'.");
        return value;
    }
}

public enum Auth0ProvisioningResult
{
    Created,
    AlreadyLinked,
    ExistingAccountRequiresLinking,
    Conflict
}

public sealed class Auth0ProvisioningService
{
    private readonly DatabaseOptions _database;

    public Auth0ProvisioningService(DatabaseOptions database) => _database = database;

    public async Task<(Auth0ProvisioningResult Result, string? UserId)> CreateStudentAsync(
        string subject,
        string email,
        string? fullName,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var mapping = connection.CreateCommand())
        {
            mapping.Transaction = transaction;
            mapping.CommandText = "SELECT UserId FROM UserIdentities WHERE Provider = 'auth0' AND ProviderSubject = @subject LIMIT 1";
            mapping.AddWithValue("@subject", subject);
            if (await mapping.ExecuteScalarAsync(cancellationToken) is string existingId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (Auth0ProvisioningResult.AlreadyLinked, existingId);
            }
        }

        await using (var existingEmail = connection.CreateCommand())
        {
            existingEmail.Transaction = transaction;
            existingEmail.CommandText = "SELECT 1 FROM Users WHERE LOWER(Email) = LOWER(@email) LIMIT 1";
            existingEmail.AddWithValue("@email", email);
            if (await existingEmail.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (Auth0ProvisioningResult.ExistingAccountRequiresLinking, null);
            }
        }

        var userId = Guid.NewGuid().ToString("N");
        var unusablePassword = BCrypt.Net.BCrypt.HashPassword(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        var createdAt = DateTime.UtcNow.ToString("o");

        try
        {
            await using var user = connection.CreateCommand();
            user.Transaction = transaction;
            user.CommandText = @"
INSERT INTO Users (Id, Email, PasswordHash, FullName, CreatedAt, IsSuperUser, IsDisabled)
VALUES (@id, @email, @password, @name, @createdAt, 0, 0);";
            user.AddWithValue("@id", userId);
            user.AddWithValue("@email", email);
            user.AddWithValue("@password", unusablePassword);
            user.AddWithValue("@name", string.IsNullOrWhiteSpace(fullName) ? DBNull.Value : fullName.Trim());
            user.AddWithValue("@createdAt", createdAt);
            await user.ExecuteNonQueryAsync(cancellationToken);

            await using var identity = connection.CreateCommand();
            identity.Transaction = transaction;
            identity.CommandText = @"
INSERT INTO UserIdentities (Provider, ProviderSubject, UserId, CreatedAt)
VALUES ('auth0', @subject, @userId, @createdAt);";
            identity.AddWithValue("@subject", subject);
            identity.AddWithValue("@userId", userId);
            identity.AddWithValue("@createdAt", createdAt);
            await identity.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (Auth0ProvisioningResult.Created, userId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            return (Auth0ProvisioningResult.Conflict, null);
        }
    }
}
