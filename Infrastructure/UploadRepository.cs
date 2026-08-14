using Microsoft.Data.Sqlite;

namespace Api.Infrastructure;

public sealed record UploadMetadata(Guid UploadId, string UserId, string FilePath, string OriginalFileName, DateTime CreatedAt);
public sealed record UploadListRecord(Guid UploadId, string UserId, string? Name, string? OriginalFileName, string CreatedAt);

public interface IUploadRepository
{
    Task CreateAsync(UploadMetadata upload, CancellationToken cancellationToken = default);
    Task<bool> CanAccessAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessClassAssignmentAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
    Task<string?> FindAccessibleClassIdAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetOwnedUploadIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UploadListRecord>> ListMineAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UploadListRecord>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<string?> GetDisplayNameAsync(Guid uploadId, CancellationToken cancellationToken = default);
    Task<List<string>?> DeleteOwnedAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
}

public sealed class SqliteUploadRepository : IUploadRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteUploadRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task CreateAsync(UploadMetadata upload, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Uploads (UploadId, UserId, FilePath, OriginalFileName, CreatedAt)
VALUES (@u, @usr, @path, @name, @ts)";
        cmd.AddWithValue("@u", upload.UploadId);
        cmd.AddWithValue("@usr", upload.UserId);
        cmd.AddWithValue("@path", upload.FilePath);
        cmd.AddWithValue("@name", upload.OriginalFileName);
        cmd.AddWithValue("@ts", upload.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CanAccessAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER(@uploadId)
  AND (
        u.UserId = @userId
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = @userId
        )
  )
LIMIT 1;
";
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@userId", userId);

        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<bool> CanAccessClassAssignmentAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM ClassStudents cs
JOIN ClassCases cc ON cc.ClassId = cs.ClassId
WHERE cs.StudentId = @studentId
  AND UPPER(cc.UploadId) = UPPER(@uploadId)
LIMIT 1;";
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@studentId", userId);

        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<string?> FindAccessibleClassIdAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT cs.ClassId
FROM ClassStudents cs
JOIN ClassCases cc ON cc.ClassId = cs.ClassId
WHERE cs.StudentId = @studentId
  AND UPPER(cc.UploadId) = UPPER(@uploadId)
ORDER BY cc.AssignedAt DESC
LIMIT 1;";
        cmd.AddWithValue("@studentId", userId);
        cmd.AddWithValue("@uploadId", uploadId.ToString());

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : Convert.ToString(result);
    }

    public async Task<HashSet<string>> GetOwnedUploadIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var uploadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT UploadId FROM Uploads WHERE UserId = @userId";
        cmd.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                var id = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    uploadIds.Add(id);
                }
            }
        }

        return uploadIds;
    }

    public async Task<List<UploadListRecord>> ListMineAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT UploadId, UserId, Name, OriginalFileName, CreatedAt
            FROM Uploads
            WHERE UserId = @me
            ORDER BY CreatedAt DESC;";
        cmd.AddWithValue("@me", userId);

        var list = new List<UploadListRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new UploadListRecord(
                UploadId: Guid.Parse(reader.GetString(0)),
                UserId: reader.GetString(1),
                Name: reader.IsDBNull(2) ? null : reader.GetString(2),
                OriginalFileName: reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt: reader.GetString(4)));
        }

        return list;
    }

    public async Task<List<UploadListRecord>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT UploadId, UserId, Name, OriginalFileName, CreatedAt
            FROM Uploads
            ORDER BY CreatedAt DESC;";

        var list = new List<UploadListRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new UploadListRecord(
                UploadId: Guid.Parse(reader.GetString(0)),
                UserId: reader.GetString(1),
                Name: reader.IsDBNull(2) ? null : reader.GetString(2),
                OriginalFileName: reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAt: reader.GetString(4)));
        }

        return list;
    }

    public async Task<string?> GetDisplayNameAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(NULLIF(Name, ''), NULLIF(OriginalFileName, ''), @fallback)
FROM Uploads
WHERE UPPER(UploadId) = UPPER(@uploadId)
LIMIT 1;";
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@fallback", $"{uploadId}.pdf");

        var resolved = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return null;
        }

        var fileName = Path.GetFileName(resolved);
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.pdf";
    }

    public async Task<List<string>?> DeleteOwnedAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();

        await using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = @"
                SELECT 1
                FROM Uploads
                WHERE UploadId = @u AND UserId = @me
                LIMIT 1;";
            chk.AddWithValue("@u", uploadId.ToString());
            chk.AddWithValue("@me", userId);

            if (await chk.ExecuteScalarAsync(cancellationToken) is null)
            {
                tx.Rollback();
                return null;
            }
        }

        var sessionIds = new List<string>();
        await using (var scmd = conn.CreateCommand())
        {
            scmd.Transaction = tx;
            scmd.CommandText = @"
                SELECT Id
                FROM Sessions
                WHERE UploadId = @u AND UserId = @me;";
            scmd.AddWithValue("@u", uploadId.ToString());
            scmd.AddWithValue("@me", userId);

            await using var reader = await scmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sessionIds.Add(reader.GetString(0));
            }
        }

        foreach (var sid in sessionIds)
        {
            await using var mcmd = conn.CreateCommand();
            mcmd.Transaction = tx;
            mcmd.CommandText = "DELETE FROM Messages WHERE SessionId = @sid";
            mcmd.AddWithValue("@sid", sid);
            await mcmd.ExecuteNonQueryAsync(cancellationToken);

            await using var ncmd = conn.CreateCommand();
            ncmd.Transaction = tx;
            ncmd.CommandText = "DELETE FROM Notes WHERE SessionId = @sid";
            ncmd.AddWithValue("@sid", sid);
            await ncmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var n2 = conn.CreateCommand())
        {
            n2.Transaction = tx;
            n2.CommandText = "DELETE FROM Notes WHERE UploadId = @u";
            n2.AddWithValue("@u", uploadId.ToString());
            await n2.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var scmd2 = conn.CreateCommand())
        {
            scmd2.Transaction = tx;
            scmd2.CommandText = "DELETE FROM Sessions WHERE UploadId = @u AND UserId = @me";
            scmd2.AddWithValue("@u", uploadId.ToString());
            scmd2.AddWithValue("@me", userId);
            await scmd2.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var ucmd = conn.CreateCommand())
        {
            ucmd.Transaction = tx;
            ucmd.CommandText = "DELETE FROM Uploads WHERE UploadId = @u AND UserId = @me";
            ucmd.AddWithValue("@u", uploadId.ToString());
            ucmd.AddWithValue("@me", userId);
            await ucmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
        return sessionIds;
    }
}

