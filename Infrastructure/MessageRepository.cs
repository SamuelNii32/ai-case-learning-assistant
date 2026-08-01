using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Infrastructure;

public sealed record CachedAnswerRecord(string Content, int[]? Citations, int[]? PagesUsed);

public interface IMessageRepository
{
    Task SaveAsync(string? sessionId, string role, string content, int[]? citations, int[]? pages, CancellationToken cancellationToken = default);
    Task<CachedAnswerRecord?> FindCachedAnswerAsync(Guid uploadId, string question, string? userId = null, CancellationToken cancellationToken = default);
    Task<string> LoadRecentConversationContextAsync(string? sessionId, CancellationToken cancellationToken = default);
}

public sealed class SqliteMessageRepository : IMessageRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteMessageRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task SaveAsync(string? sessionId, string role, string content, int[]? citations, int[]? pages, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Messages (SessionId, Role, Content, Citations, PagesUsed, CreatedAt)
            VALUES (@sid, @role, @content, @cites, @pages, @ts)";
        cmd.AddWithValue("@sid", sessionId);
        cmd.AddWithValue("@role", role);
        cmd.AddWithValue("@content", content);
        cmd.AddWithValue("@cites", citations is { Length: > 0 }
            ? JsonSerializer.Serialize(citations.Distinct())
            : DBNull.Value);
        cmd.AddWithValue("@pages", pages is { Length: > 0 }
            ? JsonSerializer.Serialize(pages.Distinct())
            : DBNull.Value);
        cmd.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CachedAnswerRecord?> FindCachedAnswerAsync(Guid uploadId, string question, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(userId))
        {
            cmd.CommandText = @"
SELECT a.Content, a.Citations, a.PagesUsed
FROM Sessions s
JOIN Messages qMsg
    ON qMsg.SessionId = s.Id
   AND qMsg.Role = 'user'
JOIN Messages a
    ON a.SessionId = s.Id
   AND a.Role = 'assistant'
   AND a.CreatedAt >= qMsg.CreatedAt
WHERE s.UploadId = @uploadId
  AND LOWER(TRIM(qMsg.Content)) = LOWER(TRIM(@q))
ORDER BY a.CreatedAt ASC
LIMIT 1;";
            cmd.AddWithValue("@uploadId", uploadId.ToString());
            cmd.AddWithValue("@q", question.Trim());
        }
        else
        {
            cmd.CommandText = @"
SELECT m.Content,
       m.Citations,
       m.PagesUsed
FROM Messages m
JOIN Sessions s ON s.Id = m.SessionId
WHERE UPPER(s.UploadId) = UPPER(@u)
  AND s.UserId   = @user
  AND m.Role     = 'assistant'
  AND EXISTS (
      SELECT 1 FROM Messages mu
      WHERE mu.SessionId = m.SessionId
        AND mu.Role      = 'user'
        AND lower(trim(mu.Content)) = lower(trim(@q))
        AND mu.Id < m.Id
  )
ORDER BY m.Id DESC
LIMIT 1;";
            cmd.AddWithValue("@u", uploadId.ToString());
            cmd.AddWithValue("@user", userId);
            cmd.AddWithValue("@q", question.Trim());
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CachedAnswerRecord(
            reader.GetString(0),
            ParseNullableIntArray(GetStringOrNull(reader, 1)),
            ParseNullableIntArray(GetStringOrNull(reader, 2)));
    }

    public async Task<string> LoadRecentConversationContextAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return "";
        }

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Role, Content
FROM (
    SELECT Id, Role, Content
    FROM Messages
    WHERE SessionId = @sid
    ORDER BY Id DESC
    LIMIT 8
)
ORDER BY Id ASC;";
        cmd.AddWithValue("@sid", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(0);
            var content = Regex.Replace(reader.GetString(1), @"\s+", " ").Trim();
            if (content.Length > 450)
            {
                content = content[..450].TrimEnd() + "...";
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                lines.Add($"{role}: {content}");
            }
        }

        return lines.Count == 0
            ? ""
            : "Recent conversation for follow-up context:\n" + string.Join("\n", lines) + "\n";
    }

    private static string? GetStringOrNull(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int[]? ParseNullableIntArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<int[]>(json);
        }
        catch
        {
            return null;
        }
    }
}

