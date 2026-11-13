using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Api.Infrastructure;

public sealed record StagingAuth0BootstrapOptions(
    bool Enabled,
    string DeploymentEnvironment,
    string Confirmation,
    string StudentSubject,
    string InstructorSubject)
{
    public const string RequiredConfirmation = "CASEPILOT-STAGING-DISPOSABLE-USERS";

    public static StagingAuth0BootstrapOptions Load(Func<string, string?>? environmentReader = null)
    {
        environmentReader ??= Environment.GetEnvironmentVariable;
        var enabledText = environmentReader("STAGING_AUTH0_BOOTSTRAP_ENABLED") ?? "false";
        if (!bool.TryParse(enabledText, out var enabled))
            throw new InvalidOperationException("STAGING_AUTH0_BOOTSTRAP_ENABLED must be 'true' or 'false'.");

        return new StagingAuth0BootstrapOptions(
            enabled,
            (environmentReader("CASEPILOT_DEPLOYMENT_ENVIRONMENT") ?? "").Trim(),
            (environmentReader("STAGING_AUTH0_BOOTSTRAP_CONFIRMATION") ?? "").Trim(),
            (environmentReader("STAGING_AUTH0_STUDENT_SUBJECT") ?? "").Trim(),
            (environmentReader("STAGING_AUTH0_INSTRUCTOR_SUBJECT") ?? "").Trim());
    }
}

public enum StagingAuth0BootstrapStatus
{
    CreatedAndLinked,
    LinkedExistingDisposableUser,
    AlreadyLinked,
    Conflict,
    InvalidExistingMapping,
    UnsafeLocalUserCollision
}

public sealed record StagingAuth0BootstrapResult(
    string Role,
    StagingAuth0BootstrapStatus Status,
    string LocalUserId);

/// <summary>
/// Trusted command-line bootstrap for disposable staging identities. This type
/// is never mapped to HTTP and never derives a local account or role from email
/// or browser input.
/// </summary>
public sealed class StagingAuth0TestUserBootstrapper
{
    private const string Provider = "auth0";
    private static readonly Regex SafeDatabaseName = new(
        @"(^|[_-])(staging|stage|test|sandbox)([_-]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly DatabaseOptions _database;
    private readonly IUserRepository _users;
    private readonly IUserIdentityRepository _identities;

    public StagingAuth0TestUserBootstrapper(
        DatabaseOptions database,
        IUserRepository users,
        IUserIdentityRepository identities)
    {
        _database = database;
        _users = users;
        _identities = identities;
    }

    public async Task<IReadOnlyList<StagingAuth0BootstrapResult>> BootstrapAsync(
        StagingAuth0BootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(options);

        var student = await BootstrapOneAsync(
            options.StudentSubject, "student", false, cancellationToken);
        var instructor = await BootstrapOneAsync(
            options.InstructorSubject, "instructor", true, cancellationToken);
        return [student, instructor];
    }

    public static string GetDisposableLocalUserId(string role, string providerSubject) =>
        CreateDisposableIdentity(role, RequireSubject(providerSubject, role)).UserId;

    private async Task<StagingAuth0BootstrapResult> BootstrapOneAsync(
        string providerSubject,
        string role,
        bool isInstructor,
        CancellationToken cancellationToken)
    {
        var identity = CreateDisposableIdentity(role, providerSubject);
        var existingMapping = await _identities.ResolveLocalUserIdAsync(
            Provider, providerSubject, cancellationToken);
        if (existingMapping is not null
            && !string.Equals(existingMapping, identity.UserId, StringComparison.Ordinal))
        {
            var conflict = await _identities.CreateAsync(
                Provider, providerSubject, identity.UserId, DateTime.UtcNow, cancellationToken);
            return new StagingAuth0BootstrapResult(
                role,
                conflict == CreateUserIdentityResult.Conflict
                    ? StagingAuth0BootstrapStatus.Conflict
                    : StagingAuth0BootstrapStatus.InvalidExistingMapping,
                identity.UserId);
        }

        var profile = await _users.GetProfileByIdAsync(identity.UserId, cancellationToken);
        var createdUser = false;
        if (profile is null)
        {
            if (existingMapping is not null)
            {
                return new StagingAuth0BootstrapResult(
                    role, StagingAuth0BootstrapStatus.InvalidExistingMapping, identity.UserId);
            }
            if (await _users.EmailExistsAsync(identity.Email, cancellationToken))
            {
                return new StagingAuth0BootstrapResult(
                    role, StagingAuth0BootstrapStatus.UnsafeLocalUserCollision, identity.UserId);
            }

            var discardedPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await _users.CreateAsync(new NewUser(
                identity.UserId,
                identity.Email,
                BCrypt.Net.BCrypt.HashPassword(discardedPassword),
                identity.FullName,
                isInstructor,
                DateTime.UtcNow), cancellationToken);
            createdUser = true;
        }
        else if (!IsExpectedDisposableUser(profile, identity, isInstructor))
        {
            return new StagingAuth0BootstrapResult(
                role, StagingAuth0BootstrapStatus.UnsafeLocalUserCollision, identity.UserId);
        }

        var mapping = await _identities.CreateAsync(
            Provider, providerSubject, identity.UserId, DateTime.UtcNow, cancellationToken);
        var status = mapping switch
        {
            CreateUserIdentityResult.Created when createdUser => StagingAuth0BootstrapStatus.CreatedAndLinked,
            CreateUserIdentityResult.Created => StagingAuth0BootstrapStatus.LinkedExistingDisposableUser,
            CreateUserIdentityResult.AlreadyExists => StagingAuth0BootstrapStatus.AlreadyLinked,
            CreateUserIdentityResult.Conflict => StagingAuth0BootstrapStatus.Conflict,
            _ => StagingAuth0BootstrapStatus.InvalidExistingMapping
        };
        return new StagingAuth0BootstrapResult(role, status, identity.UserId);
    }

    private void EnsureAuthorized(StagingAuth0BootstrapOptions options)
    {
        if (!options.Enabled)
            throw new InvalidOperationException("Staging Auth0 bootstrap is disabled.");
        if (!string.Equals(options.DeploymentEnvironment, "staging", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "CASEPILOT_DEPLOYMENT_ENVIRONMENT must be exactly 'staging'.");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(options.Confirmation),
                Encoding.UTF8.GetBytes(StagingAuth0BootstrapOptions.RequiredConfirmation)))
            throw new InvalidOperationException("The staging bootstrap confirmation is invalid.");
        if (!string.Equals(_database.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_database.Provider, "postgresql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staging Auth0 bootstrap requires PostgreSQL.");

        string databaseName;
        try
        {
            databaseName = new NpgsqlConnectionStringBuilder(_database.ConnectionString).Database ?? "";
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The staging PostgreSQL connection string is invalid.", exception);
        }
        if (!SafeDatabaseName.IsMatch(databaseName))
            throw new InvalidOperationException(
                "The PostgreSQL database name must explicitly contain staging, stage, test, or sandbox.");

        var studentSubject = RequireSubject(options.StudentSubject, "student");
        var instructorSubject = RequireSubject(options.InstructorSubject, "instructor");
        if (string.Equals(studentSubject, instructorSubject, StringComparison.Ordinal))
            throw new InvalidOperationException("Student and instructor Auth0 subjects must be different.");
    }

    private static string RequireSubject(string subject, string role)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 512)
            throw new InvalidOperationException($"A valid staging {role} Auth0 subject is required.");
        return subject.Trim();
    }

    private static DisposableIdentity CreateDisposableIdentity(string role, string providerSubject)
    {
        if (role is not ("student" or "instructor"))
            throw new ArgumentOutOfRangeException(nameof(role));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerSubject)))
            .ToLowerInvariant();
        var title = char.ToUpperInvariant(role[0]) + role[1..];
        return new DisposableIdentity(
            $"staging-auth0-{role}-{hash[..24]}",
            $"auth0-staging-{role}-{hash[..16]}@example.invalid",
            $"Disposable Auth0 Staging {title}");
    }

    private static bool IsExpectedDisposableUser(
        UserProfileRecord profile,
        DisposableIdentity identity,
        bool isInstructor) =>
        string.Equals(profile.Id, identity.UserId, StringComparison.Ordinal)
        && string.Equals(profile.Email, identity.Email, StringComparison.OrdinalIgnoreCase)
        && string.Equals(profile.FullName, identity.FullName, StringComparison.Ordinal)
        && profile.IsSuperUser == isInstructor
        && !profile.IsDisabled;

    private sealed record DisposableIdentity(string UserId, string Email, string FullName);
}
