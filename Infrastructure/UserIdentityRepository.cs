namespace Api.Infrastructure;

public enum CreateUserIdentityResult
{
    Created,
    AlreadyExists,
    Conflict,
    UserNotFound
}

public interface IUserIdentityRepository
{
    Task<string?> ResolveLocalUserIdAsync(
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default);

    Task<CreateUserIdentityResult> CreateAsync(
        string provider,
        string providerSubject,
        string userId,
        DateTime createdAt,
        CancellationToken cancellationToken = default);
}

public sealed class UserIdentityRepository : IUserIdentityRepository
{
    private readonly DatabaseOptions _databaseOptions;

    public UserIdentityRepository(DatabaseOptions databaseOptions)
    {
        _databaseOptions = databaseOptions;
    }

    public async Task<string?> ResolveLocalUserIdAsync(
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default)
    {
        provider = NormalizeProvider(provider);
        providerSubject = RequireValue(providerSubject, nameof(providerSubject));

        await using var connection = _databaseOptions.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT UserId
FROM UserIdentities
WHERE Provider = @provider AND ProviderSubject = @providerSubject
LIMIT 1;";
        command.AddWithValue("@provider", provider);
        command.AddWithValue("@providerSubject", providerSubject);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<CreateUserIdentityResult> CreateAsync(
        string provider,
        string providerSubject,
        string userId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        provider = NormalizeProvider(provider);
        providerSubject = RequireValue(providerSubject, nameof(providerSubject));
        userId = RequireValue(userId, nameof(userId));

        await using var connection = _databaseOptions.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = @"
INSERT INTO UserIdentities (Provider, ProviderSubject, UserId, CreatedAt)
SELECT @provider, @providerSubject, @userId, @createdAt
WHERE EXISTS (SELECT 1 FROM Users WHERE Id = @userId)
ON CONFLICT (Provider, ProviderSubject) DO NOTHING;";
            insert.AddWithValue("@provider", provider);
            insert.AddWithValue("@providerSubject", providerSubject);
            insert.AddWithValue("@userId", userId);
            insert.AddWithValue("@createdAt", createdAt.ToUniversalTime().ToString("o"));

            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return CreateUserIdentityResult.Created;
            }
        }

        await using (var existing = connection.CreateCommand())
        {
            existing.CommandText = @"
SELECT UserId
FROM UserIdentities
WHERE Provider = @provider AND ProviderSubject = @providerSubject
LIMIT 1;";
            existing.AddWithValue("@provider", provider);
            existing.AddWithValue("@providerSubject", providerSubject);

            if (await existing.ExecuteScalarAsync(cancellationToken) is string existingUserId)
            {
                return string.Equals(existingUserId, userId, StringComparison.Ordinal)
                    ? CreateUserIdentityResult.AlreadyExists
                    : CreateUserIdentityResult.Conflict;
            }
        }

        return CreateUserIdentityResult.UserNotFound;
    }

    private static string NormalizeProvider(string provider) =>
        RequireValue(provider, nameof(provider)).ToLowerInvariant();

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
