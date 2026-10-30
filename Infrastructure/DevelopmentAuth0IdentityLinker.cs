using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Api.Infrastructure;

public sealed record DevelopmentAuth0LinkOptions(bool Enabled, string ProviderSubject)
{
    public static DevelopmentAuth0LinkOptions Load(IConfiguration configuration)
    {
        var enabledText = Environment.GetEnvironmentVariable("DEV_AUTH0_LINK_ENABLED")
            ?? configuration["DevAuth0Link:Enabled"]
            ?? "false";
        if (!bool.TryParse(enabledText, out var enabled))
        {
            throw new InvalidOperationException("DEV_AUTH0_LINK_ENABLED must be 'true' or 'false'.");
        }

        var subject = (Environment.GetEnvironmentVariable("DEV_AUTH0_SUBJECT")
            ?? configuration["DevAuth0Link:Subject"]
            ?? "").Trim();
        return new DevelopmentAuth0LinkOptions(enabled, subject);
    }
}

public enum DevelopmentAuth0LinkStatus
{
    CreatedAndLinked,
    LinkedExistingDisposableUser,
    AlreadyLinked,
    Conflict,
    InvalidExistingMapping,
    UnsafeLocalUserCollision
}

public enum DevelopmentAuth0CleanupStatus
{
    Removed,
    NothingToRemove,
    RefusedMappingConflict,
    RefusedUnsafeLocalUser,
    RefusedRelatedData
}

public sealed record DevelopmentAuth0LinkResult(DevelopmentAuth0LinkStatus Status, string LocalUserId);
public sealed record DevelopmentAuth0CleanupResult(DevelopmentAuth0CleanupStatus Status, string LocalUserId);

public sealed class DevelopmentAuth0IdentityLinker
{
    private const string Provider = "auth0";
    private const string DisposableName = "Disposable Auth0 Development User";

    private readonly DatabaseOptions _databaseOptions;
    private readonly IUserRepository _users;
    private readonly IUserIdentityRepository _identities;

    public DevelopmentAuth0IdentityLinker(
        DatabaseOptions databaseOptions,
        IUserRepository users,
        IUserIdentityRepository identities)
    {
        _databaseOptions = databaseOptions;
        _users = users;
        _identities = identities;
    }

    public async Task<DevelopmentAuth0LinkResult> LinkAsync(
        IHostEnvironment environment,
        DevelopmentAuth0LinkOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(environment, options);
        var identity = CreateDisposableIdentity(options.ProviderSubject);
        var existingMapping = await _identities.ResolveLocalUserIdAsync(
            Provider,
            options.ProviderSubject,
            cancellationToken);

        if (existingMapping is not null
            && !string.Equals(existingMapping, identity.UserId, StringComparison.Ordinal))
        {
            var conflict = await _identities.CreateAsync(
                Provider,
                options.ProviderSubject,
                identity.UserId,
                DateTime.UtcNow,
                cancellationToken);
            return new DevelopmentAuth0LinkResult(
                conflict == CreateUserIdentityResult.Conflict
                    ? DevelopmentAuth0LinkStatus.Conflict
                    : DevelopmentAuth0LinkStatus.InvalidExistingMapping,
                identity.UserId);
        }

        var profile = await _users.GetProfileByIdAsync(identity.UserId, cancellationToken);
        var createdUser = false;
        if (profile is null)
        {
            if (existingMapping is not null)
            {
                return new DevelopmentAuth0LinkResult(
                    DevelopmentAuth0LinkStatus.InvalidExistingMapping,
                    identity.UserId);
            }

            if (await _users.EmailExistsAsync(identity.Email, cancellationToken))
            {
                return new DevelopmentAuth0LinkResult(
                    DevelopmentAuth0LinkStatus.UnsafeLocalUserCollision,
                    identity.UserId);
            }

            var discardedPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await _users.CreateAsync(new NewUser(
                identity.UserId,
                identity.Email,
                BCrypt.Net.BCrypt.HashPassword(discardedPassword),
                DisposableName,
                false,
                DateTime.UtcNow), cancellationToken);
            createdUser = true;
        }
        else if (!IsExpectedDisposableUser(profile, identity))
        {
            return new DevelopmentAuth0LinkResult(
                DevelopmentAuth0LinkStatus.UnsafeLocalUserCollision,
                identity.UserId);
        }

        var createResult = await _identities.CreateAsync(
            Provider,
            options.ProviderSubject,
            identity.UserId,
            DateTime.UtcNow,
            cancellationToken);
        var status = createResult switch
        {
            CreateUserIdentityResult.Created when createdUser => DevelopmentAuth0LinkStatus.CreatedAndLinked,
            CreateUserIdentityResult.Created => DevelopmentAuth0LinkStatus.LinkedExistingDisposableUser,
            CreateUserIdentityResult.AlreadyExists => DevelopmentAuth0LinkStatus.AlreadyLinked,
            CreateUserIdentityResult.Conflict => DevelopmentAuth0LinkStatus.Conflict,
            _ => DevelopmentAuth0LinkStatus.InvalidExistingMapping
        };
        return new DevelopmentAuth0LinkResult(status, identity.UserId);
    }

    public async Task<DevelopmentAuth0CleanupResult> CleanupAsync(
        IHostEnvironment environment,
        DevelopmentAuth0LinkOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(environment, options);
        var identity = CreateDisposableIdentity(options.ProviderSubject);
        var mapping = await _identities.ResolveLocalUserIdAsync(
            Provider,
            options.ProviderSubject,
            cancellationToken);
        if (mapping is not null && !string.Equals(mapping, identity.UserId, StringComparison.Ordinal))
        {
            return new DevelopmentAuth0CleanupResult(
                DevelopmentAuth0CleanupStatus.RefusedMappingConflict,
                identity.UserId);
        }

        var profile = await _users.GetProfileByIdAsync(identity.UserId, cancellationToken);
        if (profile is null)
        {
            return new DevelopmentAuth0CleanupResult(
                DevelopmentAuth0CleanupStatus.NothingToRemove,
                identity.UserId);
        }

        if (!IsExpectedDisposableUser(profile, identity))
        {
            return new DevelopmentAuth0CleanupResult(
                DevelopmentAuth0CleanupStatus.RefusedUnsafeLocalUser,
                identity.UserId);
        }

        await using var connection = _databaseOptions.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await HasRelatedDataAsync(
                connection,
                transaction,
                identity.UserId,
                options.ProviderSubject,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DevelopmentAuth0CleanupResult(
                DevelopmentAuth0CleanupStatus.RefusedRelatedData,
                identity.UserId);
        }

        await using (var deleteMapping = connection.CreateCommand())
        {
            deleteMapping.Transaction = transaction;
            deleteMapping.CommandText = @"
DELETE FROM UserIdentities
WHERE Provider = @provider AND ProviderSubject = @subject AND UserId = @userId;";
            deleteMapping.AddWithValue("@provider", Provider);
            deleteMapping.AddWithValue("@subject", options.ProviderSubject);
            deleteMapping.AddWithValue("@userId", identity.UserId);
            await deleteMapping.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteUser = connection.CreateCommand())
        {
            deleteUser.Transaction = transaction;
            deleteUser.CommandText = "DELETE FROM Users WHERE Id = @userId;";
            deleteUser.AddWithValue("@userId", identity.UserId);
            await deleteUser.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DevelopmentAuth0CleanupResult(DevelopmentAuth0CleanupStatus.Removed, identity.UserId);
    }

    public static string GetDisposableLocalUserId(string providerSubject) =>
        CreateDisposableIdentity(RequireSubject(providerSubject)).UserId;

    private void EnsureAuthorized(IHostEnvironment environment, DevelopmentAuth0LinkOptions options)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Disposable Auth0 identity linking is available only in Development.");
        }

        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "Disposable Auth0 identity linking is disabled. Set DevAuth0Link:Enabled only in user-secrets.");
        }

        if (!string.Equals(_databaseOptions.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Disposable Auth0 identity linking is restricted to the SQLite development database.");
        }

        RequireSubject(options.ProviderSubject);
    }

    private static string RequireSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 512)
        {
            throw new InvalidOperationException("A valid DevAuth0Link:Subject is required.");
        }

        return subject.Trim();
    }

    private static DisposableIdentity CreateDisposableIdentity(string providerSubject)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerSubject)))
            .ToLowerInvariant();
        return new DisposableIdentity(
            $"dev-auth0-{hash[..24]}",
            $"auth0-dev-{hash[..16]}@example.invalid");
    }

    private static bool IsExpectedDisposableUser(UserProfileRecord profile, DisposableIdentity identity) =>
        string.Equals(profile.Id, identity.UserId, StringComparison.Ordinal)
        && string.Equals(profile.Email, identity.Email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(profile.FullName, DisposableName, StringComparison.Ordinal)
        && !profile.IsSuperUser;

    private static async Task<bool> HasRelatedDataAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string userId,
        string providerSubject,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT
  (SELECT COUNT(*) FROM Uploads WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM Sessions WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM Notes WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM Classes WHERE InstructorId = @userId) +
  (SELECT COUNT(*) FROM ClassStudents WHERE StudentId = @userId) +
  (SELECT COUNT(*) FROM TutorSessions WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM TutorAnswers WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM TutorHelpEvents WHERE UserId = @userId) +
  (SELECT COUNT(*) FROM IndexJobs WHERE RequestedBy = @userId) +
  (SELECT COUNT(*) FROM UserIdentities
   WHERE UserId = @userId
     AND NOT (Provider = @provider AND ProviderSubject = @subject));";
        command.AddWithValue("@userId", userId);
        command.AddWithValue("@provider", Provider);
        command.AddWithValue("@subject", providerSubject);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private sealed record DisposableIdentity(string UserId, string Email);
}
