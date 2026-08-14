using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace Api.Infrastructure;

public sealed record SessionMineRecord(
    string SessionId,
    string? UploadId,
    string CaseName,
    string? CreatedAt,
    string? LastActivityAt,
    int DurationSec,
    int MessageCount,
    int NotesCount,
    string? LastMessagePreview);

public sealed record SessionMessageRecord(
    string Role,
    string Content,
    int[] Citations,
    int[] PagesUsed,
    string CreatedAt);

public sealed record SessionNoteRecord(long Id, string Text, string CreatedAt);

public sealed record AdminSessionRecord(
    string SessionId,
    string UserId,
    string UserEmail,
    string UserFullName,
    string UploadId,
    string CaseName,
    string OriginalFileName,
    string SessionCreatedAt,
    string? LastMessageAt,
    int MessageCount);

public sealed record AdminSessionMessageRecord(
    long Id,
    string Role,
    string Content,
    string? Citations,
    string? PagesUsed,
    string CreatedAt);

public sealed record AdminSessionDetailRecord(
    string SessionId,
    string UserId,
    string UserEmail,
    string UserFullName,
    string? UploadId,
    string? CaseName,
    string? OriginalFileName,
    string CreatedAt,
    List<AdminSessionMessageRecord> Messages);

public sealed record DebugSessionRecord(string SessionId, string UserId, string? UploadId, string? ClassId, string CreatedAt);

public interface ISessionRepository
{
    Task<PagedResult<SessionMineRecord>> ListMineAsync(
        string userId,
        int page,
        int pageSize,
        string? query = null,
        string? uploadId = null,
        CancellationToken cancellationToken = default);
    Task CreateAsync(string sessionId, string userId, string? uploadId, DateTime createdAt, string? classId = null, CancellationToken cancellationToken = default);
    Task<string?> GetOwnedSessionUploadIdAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
    Task<List<SessionMessageRecord>?> GetOwnedMessagesAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
    Task<List<SessionNoteRecord>?> ListNotesAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
    Task<SessionNoteRecord?> AddNoteAsync(string sessionId, string userId, string text, CancellationToken cancellationToken = default);
    Task<bool?> UpdateNoteAsync(string sessionId, string userId, long noteId, string text, CancellationToken cancellationToken = default);
    Task<bool?> DeleteNoteAsync(string sessionId, string userId, long noteId, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminSessionRecord>> ListAdminSessionsAsync(
        string instructorId,
        int page,
        int pageSize,
        string? query = null,
        CancellationToken cancellationToken = default);
    Task<AdminSessionDetailRecord?> GetAdminSessionAsync(string sessionId, string instructorId, CancellationToken cancellationToken = default);
    Task<List<DebugSessionRecord>> ListAllSessionsAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteSessionRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task<PagedResult<SessionMineRecord>> ListMineAsync(
        string userId,
        int page,
        int pageSize,
        string? query = null,
        string? uploadId = null,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var offset = Pagination.Offset(page, pageSize);
        var searchPattern = Pagination.BuildLikePattern(query);
        var hasSearch = !string.IsNullOrWhiteSpace(query);
        var hasUploadId = !string.IsNullOrWhiteSpace(uploadId);

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var baseSql = new StringBuilder(@"
SELECT
    s.Id AS SessionId,
    s.UploadId,
    COALESCE(NULLIF(u.Name, ''), NULLIF(u.OriginalFileName, ''), 'Untitled case') AS CaseName,
    MIN(COALESCE(m.CreatedAt, s.CreatedAt)) AS CreatedAt,
    MAX(COALESCE(m.CreatedAt, s.CreatedAt)) AS LastActivityAt,
    COUNT(m.Id) AS MessageCount,
    (
        SELECT COUNT(1)
        FROM Notes n
        WHERE n.SessionId = s.Id
    ) AS NotesCount,
    substr(
      COALESCE(
        (
          SELECT m2.Content
          FROM Messages m2
          WHERE m2.SessionId = s.Id
            AND (m2.Role = 'user' OR m2.Role IS NULL OR m2.Role = '')
          ORDER BY m2.CreatedAt DESC, m2.Id DESC
          LIMIT 1
        ),
        (
          SELECT m3.Content
          FROM Messages m3
          WHERE m3.SessionId = s.Id
          ORDER BY m3.CreatedAt DESC, m3.Id DESC
          LIMIT 1
        )
      ),
      1,
      80
    ) AS LastMessagePreview
FROM Sessions s
LEFT JOIN Uploads u ON u.UploadId = s.UploadId
LEFT JOIN Messages m ON m.SessionId = s.Id
WHERE s.UserId = @me");

        if (hasUploadId)
        {
            baseSql.Append(" AND s.UploadId = @uploadId");
        }

        if (hasSearch)
        {
            baseSql.Append(@"
 AND (
    LOWER(COALESCE(NULLIF(u.Name, ''), NULLIF(u.OriginalFileName, ''), 'Untitled case')) LIKE @search ESCAPE '\'
    OR LOWER(COALESCE(
        (
            SELECT m2.Content
            FROM Messages m2
            WHERE m2.SessionId = s.Id
            ORDER BY m2.CreatedAt DESC, m2.Id DESC
            LIMIT 1
        ),
        ''
    )) LIKE @search ESCAPE '\'
 )");
        }

        baseSql.Append(@"
GROUP BY
    s.Id,
    s.UploadId,
    COALESCE(u.Name, u.OriginalFileName, 'Untitled case')");

        var countSql = $"WITH base AS ({baseSql}) SELECT COUNT(1) FROM base;";
        var pageSql = $"WITH base AS ({baseSql}) SELECT * FROM base ORDER BY LastActivityAt DESC, SessionId DESC LIMIT @limit OFFSET @offset;";

        long totalCount;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = countSql;
            countCmd.AddWithValue("@me", userId);
            if (hasUploadId)
            {
                countCmd.AddWithValue("@uploadId", uploadId!);
            }
            if (hasSearch)
            {
                countCmd.AddWithValue("@search", searchPattern);
            }

            totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var sessions = new List<SessionMineRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pageSql;
            cmd.AddWithValue("@me", userId);
            cmd.AddWithValue("@limit", pageSize);
            cmd.AddWithValue("@offset", offset);
            if (hasUploadId)
            {
                cmd.AddWithValue("@uploadId", uploadId!);
            }
            if (hasSearch)
            {
                cmd.AddWithValue("@search", searchPattern);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var createdAt = reader.IsDBNull(3) ? null : reader.GetString(3);
                var lastActivityAt = reader.IsDBNull(4) ? null : reader.GetString(4);
                var durationSec = 0;
                if (DateTime.TryParse(createdAt, out var created) &&
                    DateTime.TryParse(lastActivityAt, out var lastActivity))
                {
                    durationSec = (int)Math.Max(0, (lastActivity - created).TotalSeconds);
                }

                sessions.Add(new SessionMineRecord(
                    SessionId: reader.GetString(0),
                    UploadId: reader.IsDBNull(1) ? null : reader.GetString(1),
                    CaseName: reader.IsDBNull(2) ? "Untitled case" : reader.GetString(2),
                    CreatedAt: createdAt,
                    LastActivityAt: lastActivityAt,
                    DurationSec: durationSec,
                    MessageCount: reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                    NotesCount: reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6)),
                    LastMessagePreview: reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        return new PagedResult<SessionMineRecord>(sessions, page, pageSize, (int)Math.Min(int.MaxValue, totalCount));
    }

    public async Task CreateAsync(string sessionId, string userId, string? uploadId, DateTime createdAt, string? classId = null, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Sessions (Id, UserId, UploadId, ClassId, CreatedAt)
                            VALUES (@id, @user, @upload, @classId, @ts)";
        cmd.AddWithValue("@id", sessionId);
        cmd.AddWithValue("@user", userId);
        cmd.AddWithValue("@upload", (object?)uploadId ?? DBNull.Value);
        cmd.AddWithValue("@classId", (object?)classId ?? DBNull.Value);
        cmd.AddWithValue("@ts", createdAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetOwnedSessionUploadIdAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT UploadId
            FROM Sessions
            WHERE Id = @id AND UserId = @me
            LIMIT 1";
        cmd.AddWithValue("@id", sessionId);
        cmd.AddWithValue("@me", userId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : Convert.ToString(result);
    }

    public async Task<List<SessionMessageRecord>?> GetOwnedMessagesAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        SELECT Role, Content, Citations, PagesUsed, CreatedAt
        FROM Messages
        WHERE SessionId = @id
        ORDER BY CreatedAt) ASC, Id ASC";
        cmd.AddWithValue("@id", sessionId);

        var messages = new List<SessionMessageRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new SessionMessageRecord(
                Role: reader.GetString(0),
                Content: reader.GetString(1),
                Citations: DeserializeIntArray(reader, 2),
                PagesUsed: DeserializeIntArray(reader, 3),
                CreatedAt: reader.GetString(4)));
        }

        return messages;
    }

    public async Task<List<SessionNoteRecord>?> ListNotesAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        SELECT Id, Text, CreatedAt
        FROM Notes
        WHERE SessionId = @id AND UserId = @me
        ORDER BY CreatedAt) ASC, Id ASC";
        cmd.AddWithValue("@id", sessionId);
        cmd.AddWithValue("@me", userId);

        var notes = new List<SessionNoteRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            notes.Add(new SessionNoteRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return notes;
    }

    public async Task<SessionNoteRecord?> AddNoteAsync(string sessionId, string userId, string text, CancellationToken cancellationToken = default)
    {
        var uploadId = await GetOwnedSessionUploadIdAsync(sessionId, userId, cancellationToken);
        if (uploadId is null && !await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var createdAt = DateTime.UtcNow.ToString("o");
        await using var idCmd = conn.CreateCommand();
        idCmd.CommandText = @"
            INSERT INTO Notes (UserId, SessionId, UploadId, Text, CreatedAt)
            VALUES (@userId, @sessionId, @uploadId, @text, @createdAt)
            RETURNING Id;";
        idCmd.AddWithValue("@userId", userId);
        idCmd.AddWithValue("@sessionId", sessionId);
        idCmd.AddWithValue("@uploadId", (object?)uploadId ?? DBNull.Value);
        idCmd.AddWithValue("@text", text);
        idCmd.AddWithValue("@createdAt", createdAt);
        var scalar = await idCmd.ExecuteScalarAsync(cancellationToken);
        var noteId = scalar is long l ? l : Convert.ToInt64(scalar);

        return new SessionNoteRecord(noteId, text, createdAt);
    }

    public async Task<bool?> UpdateNoteAsync(string sessionId, string userId, long noteId, string text, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        UPDATE Notes
        SET Text = @text
        WHERE Id = @noteId AND SessionId = @sessionId AND UserId = @userId";
        cmd.AddWithValue("@text", text);
        cmd.AddWithValue("@noteId", noteId);
        cmd.AddWithValue("@sessionId", sessionId);
        cmd.AddWithValue("@userId", userId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool?> DeleteNoteAsync(string sessionId, string userId, long noteId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        DELETE FROM Notes
        WHERE Id = @noteId AND SessionId = @sessionId AND UserId = @userId";
        cmd.AddWithValue("@noteId", noteId);
        cmd.AddWithValue("@sessionId", sessionId);
        cmd.AddWithValue("@userId", userId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
    {
        if (!await SessionExistsAsync(sessionId, userId, cancellationToken))
        {
            return false;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var delMsg = conn.CreateCommand())
        {
            delMsg.CommandText = "DELETE FROM Messages WHERE SessionId = @id";
            delMsg.AddWithValue("@id", sessionId);
            await delMsg.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delNotes = conn.CreateCommand())
        {
            delNotes.CommandText = "DELETE FROM Notes WHERE SessionId = @id";
            delNotes.AddWithValue("@id", sessionId);
            await delNotes.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delSession = conn.CreateCommand())
        {
            delSession.CommandText = "DELETE FROM Sessions WHERE Id = @id AND UserId = @me";
            delSession.AddWithValue("@id", sessionId);
            delSession.AddWithValue("@me", userId);
            await delSession.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    public async Task<PagedResult<AdminSessionRecord>> ListAdminSessionsAsync(
        string instructorId,
        int page,
        int pageSize,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var offset = Pagination.Offset(page, pageSize);
        var searchPattern = Pagination.BuildLikePattern(query);
        var hasSearch = !string.IsNullOrWhiteSpace(query);

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var baseSql = new StringBuilder(@"
SELECT DISTINCT
    s.Id                AS SessionId,
    s.UserId            AS UserId,
    COALESCE(u.Email, '') AS UserEmail,
    COALESCE(u.FullName,'') AS UserFullName,
    s.UploadId          AS UploadId,
    COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), '') AS CaseName,
    COALESCE(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt         AS SessionCreatedAt,
    (
        SELECT MAX(m.CreatedAt)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS LastMessageAt,
    (
        SELECT COUNT(1)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS MessageCount
FROM Sessions s
JOIN Classes c ON c.Id = s.ClassId
    AND c.InstructorId = @instructorId
LEFT JOIN Users   u  ON u.Id       = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
WHERE c.InstructorId = @instructorId");

        if (hasSearch)
        {
            baseSql.Append(@"
  AND (
    LOWER(COALESCE(u.Email, '')) LIKE @search ESCAPE '\'
    OR LOWER(COALESCE(u.FullName, '')) LIKE @search ESCAPE '\'
    OR LOWER(COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), '')) LIKE @search ESCAPE '\'
    OR LOWER(COALESCE(up.OriginalFileName, '')) LIKE @search ESCAPE '\'
  )");
        }
        var countSql = $"WITH base AS ({baseSql}) SELECT COUNT(1) FROM base;";
        var pageSql = $"WITH base AS ({baseSql}) SELECT * FROM base ORDER BY SessionCreatedAt DESC, SessionId DESC LIMIT @limit OFFSET @offset;";

        long totalCount;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = countSql;
            countCmd.AddWithValue("@instructorId", instructorId);
            if (hasSearch)
            {
                countCmd.AddWithValue("@search", searchPattern);
            }
            totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var list = new List<AdminSessionRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pageSql;
            cmd.AddWithValue("@instructorId", instructorId);
            cmd.AddWithValue("@limit", pageSize);
            cmd.AddWithValue("@offset", offset);
            if (hasSearch)
            {
                cmd.AddWithValue("@search", searchPattern);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new AdminSessionRecord(
                    SessionId: reader.GetString(0),
                    UserId: reader.GetString(1),
                    UserEmail: reader.GetString(2),
                    UserFullName: reader.GetString(3),
                    UploadId: reader.IsDBNull(4) ? "" : reader.GetString(4),
                    CaseName: reader.GetString(5),
                    OriginalFileName: reader.GetString(6),
                    SessionCreatedAt: reader.GetString(7),
                    LastMessageAt: reader.IsDBNull(8) ? null : reader.GetString(8),
                    MessageCount: reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9))));
            }
        }

        return new PagedResult<AdminSessionRecord>(list, page, pageSize, (int)Math.Min(int.MaxValue, totalCount));
    }

    public async Task<AdminSessionDetailRecord?> GetAdminSessionAsync(string sessionId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var metaCmd = conn.CreateCommand();
        metaCmd.CommandText = @"
SELECT
    s.Id                AS SessionId,
    s.UserId            AS UserId,
    COALESCE(u.Email, '') AS UserEmail,
    COALESCE(u.FullName,'') AS UserFullName,
    s.UploadId          AS UploadId,
    COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), '') AS CaseName,
    COALESCE(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt         AS SessionCreatedAt
FROM Sessions s
JOIN Classes cls ON cls.Id = s.ClassId
LEFT JOIN Users   u  ON u.Id        = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
WHERE s.Id = @id
  AND cls.InstructorId = @instructorId
LIMIT 1;";
        metaCmd.AddWithValue("@id", sessionId);
        metaCmd.AddWithValue("@instructorId", instructorId);

        string userId;
        string userEmail;
        string userFullName;
        string? uploadId;
        string? caseName;
        string? originalFileName;
        string createdAt;

        await using (var reader = await metaCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            userId = reader.GetString(1);
            userEmail = reader.GetString(2);
            userFullName = reader.GetString(3);
            uploadId = reader.IsDBNull(4) ? null : reader.GetString(4);
            caseName = reader.IsDBNull(5) ? null : reader.GetString(5);
            originalFileName = reader.IsDBNull(6) ? null : reader.GetString(6);
            createdAt = reader.GetString(7);
        }

        if (uploadId is null)
        {
            return null;
        }

        var messages = new List<AdminSessionMessageRecord>();
        await using var msgCmd = conn.CreateCommand();
        msgCmd.CommandText = @"
SELECT
    Id,
    Role,
    Content,
    Citations,
    PagesUsed,
    CreatedAt
FROM Messages
WHERE SessionId = @id
ORDER BY Id ASC;";
        msgCmd.AddWithValue("@id", sessionId);

        await using (var reader = await msgCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new AdminSessionMessageRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        return new AdminSessionDetailRecord(
            SessionId: sessionId,
            UserId: userId,
            UserEmail: userEmail,
            UserFullName: userFullName,
            UploadId: uploadId,
            CaseName: caseName,
            OriginalFileName: originalFileName,
            CreatedAt: createdAt,
            Messages: messages);
    }

    public async Task<List<DebugSessionRecord>> ListAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, UserId, UploadId, ClassId, CreatedAt FROM Sessions ORDER BY CreatedAt DESC;";

        var list = new List<DebugSessionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new DebugSessionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return list;
    }

    private async Task<bool> SessionExistsAsync(string sessionId, string userId, CancellationToken cancellationToken)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Sessions WHERE Id = @id AND UserId = @me LIMIT 1";
        cmd.AddWithValue("@id", sessionId);
        cmd.AddWithValue("@me", userId);

        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static int[] DeserializeIntArray(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return Array.Empty<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<int[]>(reader.GetString(ordinal)) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }
}

