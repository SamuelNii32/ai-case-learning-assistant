using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public static class TutorSessionPersistence
{
    public static async Task<TutorSession?> TryLoadLatestReadingAsync(string connString, Guid uploadId, string userId)
    {
        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT SessionId
FROM TutorSessions
WHERE UserId = $userId
  AND UPPER(UploadId) = UPPER($uploadId)
  AND Focus = 'reading_coach'
ORDER BY UpdatedAt DESC
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

        var sessionId = await cmd.ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : await TryLoadAsync(connString, sessionId);
    }

    public static async Task<TutorSession?> TryLoadAsync(string connString, string sessionId)
    {
        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  SessionId,
  UploadId,
  Category,
  Focus,
  CurrentNode,
  VisitedTopicsJson,
  VisitedPagesJson,
  HistoryJson,
  LastStepSummary,
  DrillPathJson,
  PendingDrillChoicesJson
FROM TutorSessions
WHERE SessionId = $sessionId
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var uploadId = Guid.Parse(reader.GetString(reader.GetOrdinal("UploadId")));
        var categoryText = reader.GetString(reader.GetOrdinal("Category"));
        if (!Enum.TryParse<DocType>(categoryText, out var category))
        {
            category = DocType.UnsupportedOther;
        }

        return new TutorSession(
            SessionId: reader.GetString(reader.GetOrdinal("SessionId")),
            UploadId: uploadId,
            Category: category,
            Focus: GetNullableString(reader, "Focus"),
            CurrentNode: reader.GetString(reader.GetOrdinal("CurrentNode")),
            VisitedTopics: DeserializeList<string>(GetNullableString(reader, "VisitedTopicsJson")),
            VisitedPages: DeserializeList<int>(GetNullableString(reader, "VisitedPagesJson")),
            History: DeserializeList<string>(GetNullableString(reader, "HistoryJson")),
            LastStepSummary: GetNullableString(reader, "LastStepSummary"),
            DrillPath: DeserializeList<TutorDrillNode>(GetNullableString(reader, "DrillPathJson")),
            PendingDrillChoices: DeserializeList<TutorDrillNode>(GetNullableString(reader, "PendingDrillChoicesJson"))
        );
    }

    public static async Task SaveAsync(string connString, TutorSession session, string userId)
    {
        var now = DateTime.UtcNow.ToString("O");

        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO TutorSessions (
  SessionId,
  UserId,
  UploadId,
  Category,
  Focus,
  CurrentNode,
  VisitedTopicsJson,
  VisitedPagesJson,
  HistoryJson,
  LastStepSummary,
  DrillPathJson,
  PendingDrillChoicesJson,
  CreatedAt,
  UpdatedAt
)
VALUES (
  $sessionId,
  $userId,
  $uploadId,
  $category,
  $focus,
  $currentNode,
  $visitedTopicsJson,
  $visitedPagesJson,
  $historyJson,
  $lastStepSummary,
  $drillPathJson,
  $pendingDrillChoicesJson,
  $createdAt,
  $updatedAt
)
ON CONFLICT(SessionId) DO UPDATE SET
  UserId = excluded.UserId,
  UploadId = excluded.UploadId,
  Category = excluded.Category,
  Focus = excluded.Focus,
  CurrentNode = excluded.CurrentNode,
  VisitedTopicsJson = excluded.VisitedTopicsJson,
  VisitedPagesJson = excluded.VisitedPagesJson,
  HistoryJson = excluded.HistoryJson,
  LastStepSummary = excluded.LastStepSummary,
  DrillPathJson = excluded.DrillPathJson,
  PendingDrillChoicesJson = excluded.PendingDrillChoicesJson,
  UpdatedAt = excluded.UpdatedAt;
";

        cmd.Parameters.AddWithValue("$sessionId", session.SessionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$uploadId", session.UploadId.ToString());
        cmd.Parameters.AddWithValue("$category", session.Category.ToString());
        cmd.Parameters.AddWithValue("$focus", (object?)session.Focus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$currentNode", session.CurrentNode);
        cmd.Parameters.AddWithValue("$visitedTopicsJson", JsonSerializer.Serialize(session.VisitedTopics));
        cmd.Parameters.AddWithValue("$visitedPagesJson", JsonSerializer.Serialize(session.VisitedPages));
        cmd.Parameters.AddWithValue("$historyJson", JsonSerializer.Serialize(session.History));
        cmd.Parameters.AddWithValue("$lastStepSummary", (object?)session.LastStepSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$drillPathJson", JsonSerializer.Serialize(session.DrillPath ?? new List<TutorDrillNode>()));
        cmd.Parameters.AddWithValue("$pendingDrillChoicesJson", JsonSerializer.Serialize(session.PendingDrillChoices ?? new List<TutorDrillNode>()));
        cmd.Parameters.AddWithValue("$createdAt", now);
        cmd.Parameters.AddWithValue("$updatedAt", now);

        await cmd.ExecuteNonQueryAsync();
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }
}
