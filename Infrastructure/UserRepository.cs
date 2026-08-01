using Microsoft.Data.Sqlite;

namespace Api.Infrastructure;

public sealed record NewUser(
    string Id,
    string Email,
    string PasswordHash,
    string? FullName,
    bool IsSuperUser,
    DateTime CreatedAt);

public sealed record UserCredentialRecord(
    string Id,
    string Email,
    string PasswordHash,
    string FullName,
    bool IsSuperUser);

public sealed record UserProfileRecord(
    string Id,
    string Email,
    string FullName,
    bool IsSuperUser);

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(NewUser user, CancellationToken cancellationToken = default);
    Task<UserCredentialRecord?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<UserProfileRecord?> GetProfileByIdAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class SqliteUserRepository : IUserRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteUserRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Users WHERE Email = @e LIMIT 1";
        cmd.AddWithValue("@e", email);

        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task CreateAsync(NewUser user, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Users (Id, Email, PasswordHash, FullName, CreatedAt, IsSuperUser)
VALUES (@id,@e,@h,@n,@t,@su)";
        cmd.AddWithValue("@id", user.Id);
        cmd.AddWithValue("@e", user.Email);
        cmd.AddWithValue("@h", user.PasswordHash);
        cmd.AddWithValue("@n", string.IsNullOrWhiteSpace(user.FullName) ? DBNull.Value : user.FullName);
        cmd.AddWithValue("@t", user.CreatedAt.ToString("o"));
        cmd.AddWithValue("@su", user.IsSuperUser ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<UserCredentialRecord?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, PasswordHash, COALESCE(FullName,''), COALESCE(IsSuperUser,0) FROM Users WHERE Email = @e LIMIT 1";
        cmd.AddWithValue("@e", email);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserCredentialRecord(
            Id: reader.GetString(0),
            Email: email,
            PasswordHash: reader.GetString(1),
            FullName: reader.GetString(2),
            IsSuperUser: reader.GetInt32(3) != 0);
    }

    public async Task<UserProfileRecord?> GetProfileByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Email,
       COALESCE(FullName, ''),
       COALESCE(IsSuperUser, 0)
FROM Users
WHERE Id = @id
LIMIT 1;";
        cmd.AddWithValue("@id", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserProfileRecord(
            Id: userId,
            Email: reader.GetString(0),
            FullName: reader.GetString(1),
            IsSuperUser: reader.GetInt32(2) != 0);
    }
}

