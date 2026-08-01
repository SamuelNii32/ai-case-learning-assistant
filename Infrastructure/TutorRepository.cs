using Microsoft.Data.Sqlite;

namespace Api.Infrastructure;

public sealed record ReadingAssignmentDataRecord(string? Objective, string? Focus, string? DueAt, string? ReadingCoachQuestions);

public sealed record ReadingAnswerDataRecord(
    string StepId,
    string Question,
    string Answer,
    double Score,
    string? Verdict,
    string? Hint);

public sealed record ReadingPerformanceDataRecord(
    string CategoryText,
    int CompletedSteps,
    int TotalSteps,
    int AnswerAttempts,
    int WeakAttempts,
    int HelpRequests,
    List<ReadingAnswerDataRecord> Answers,
    List<string> HelpQuestions);

public sealed record ClassReadingCoachSummaryRecord(
    string ClassId,
    int AssignedStudents,
    int AssignedCases,
    int StartedStudents,
    int ActiveStudents24h,
    int HelpRequests24h,
    int ChatMessages24h,
    int TutorAnswers24h,
    string GeneratedAt);

public sealed record TutorProgressRecord(
    string StudentId,
    string StudentName,
    string StudentEmail,
    string UploadId,
    string FileName,
    string ReadingCategory,
    int CompletedSteps,
    int TotalSteps,
    int AnswerAttempts,
    int WeakAttempts,
    int HelpRequests,
    double AverageScore,
    string? LastActivity,
    string? LatestTutorSessionId,
    string Status,
    object? CurrentStep,
    object? LastWeakStep,
    string? LastHelpQuestion);

public sealed record TutorProgressDetailAnswerRecord(
    long Id,
    string SessionId,
    string StepId,
    string StepTitle,
    string Question,
    string Answer,
    string Feedback,
    object? FeedbackSummary,
    double Score,
    string CreatedAt);

public sealed record TutorProgressDetailHelpEventRecord(
    long Id,
    string? ChatSessionId,
    string? TutorSessionId,
    string? StepId,
    string Question,
    string CreatedAt);

public sealed record TutorProgressDetailRecord(
    string ClassId,
    string StudentId,
    Guid UploadId,
    object? Student,
    object? CaseInfo,
    string? StudentName,
    string? StudentEmail,
    string? CaseName,
    string? CaseFileName,
    string ReadingCategory,
    int TotalSteps,
    int CompletedSteps,
    int AnswerAttempts,
    int WeakAttempts,
    int HelpRequests,
    string? LatestTutorSessionId,
    string Status,
    object? CurrentStep,
    object? LastWeakStep,
    string? LastHelpQuestion,
    bool NeedsAttention,
    List<TutorProgressDetailAnswerRecord> Answers,
    List<TutorProgressDetailHelpEventRecord> HelpEvents);

public interface ITutorRepository
{
    Task SaveHelpEventAsync(string userId, Guid uploadId, string? chatSessionId, string? tutorSessionId, string? stepId, string question, CancellationToken cancellationToken = default);
    Task<ReadingAssignmentDataRecord?> LoadReadingAssignmentContextAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
    Task<ReadingPerformanceDataRecord> LoadReadingPerformanceSnapshotAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default);
    Task<List<TutorProgressRecord>> ListClassProgressAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<ClassReadingCoachSummaryRecord?> GetClassReadingCoachSummaryAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<TutorProgressDetailRecord?> GetTutorProgressDetailAsync(string classId, string instructorId, string studentId, Guid uploadId, CancellationToken cancellationToken = default);
}

public sealed class SqliteTutorRepository : ITutorRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteTutorRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task SaveHelpEventAsync(string userId, Guid uploadId, string? chatSessionId, string? tutorSessionId, string? stepId, string question, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO TutorHelpEvents (
  UserId,
  UploadId,
  ChatSessionId,
  TutorSessionId,
  StepId,
  Question,
  CreatedAt
)
VALUES (
  @userId,
  @uploadId,
  @chatSessionId,
  @tutorSessionId,
  @stepId,
  @question,
  @createdAt
);";
        cmd.AddWithValue("@userId", userId);
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@chatSessionId", (object?)chatSessionId ?? DBNull.Value);
        cmd.AddWithValue("@tutorSessionId", (object?)tutorSessionId ?? DBNull.Value);
        cmd.AddWithValue("@stepId", (object?)stepId ?? DBNull.Value);
        cmd.AddWithValue("@question", question);
        cmd.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReadingAssignmentDataRecord?> LoadReadingAssignmentContextAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT cc.Objective, cc.Focus, cc.DueAt, cc.ReadingCoachQuestions
FROM ClassCases cc
JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
WHERE cs.StudentId = @userId
  AND UPPER(cc.UploadId) = UPPER(@uploadId)
ORDER BY cc.AssignedAt DESC
LIMIT 1;";
        cmd.AddWithValue("@userId", userId);
        cmd.AddWithValue("@uploadId", uploadId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var objective = reader.IsDBNull(0) ? null : reader.GetString(0);
        var focus = reader.IsDBNull(1) ? null : reader.GetString(1);
        var dueAt = reader.IsDBNull(2) ? null : reader.GetString(2);
        var readingCoachQuestions = reader.IsDBNull(3) ? null : reader.GetString(3);

        return string.IsNullOrWhiteSpace(objective) &&
            string.IsNullOrWhiteSpace(focus) &&
            string.IsNullOrWhiteSpace(dueAt) &&
            string.IsNullOrWhiteSpace(readingCoachQuestions)
            ? null
            : new ReadingAssignmentDataRecord(objective, focus, dueAt, readingCoachQuestions);
    }

    public async Task<ReadingPerformanceDataRecord> LoadReadingPerformanceSnapshotAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var categoryText = "AcademicResearch";
        await using (var categoryCmd = conn.CreateCommand())
        {
            categoryCmd.CommandText = @"
SELECT Category
FROM TutorSessions
WHERE UserId = @userId
  AND UPPER(UploadId) = UPPER(@uploadId)
  AND Focus = 'reading_coach'
ORDER BY UpdatedAt DESC
LIMIT 1;";
            categoryCmd.AddWithValue("@userId", userId);
            categoryCmd.AddWithValue("@uploadId", uploadId.ToString());
            var category = await categoryCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(category))
            {
                categoryText = category;
            }
        }

        var answers = new List<ReadingAnswerDataRecord>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weakAttempts = 0;

        await using (var answerCmd = conn.CreateCommand())
        {
            answerCmd.CommandText = @"
SELECT StepId, Question, Answer, Feedback, Score
FROM TutorAnswers
WHERE UserId = @userId
  AND UPPER(UploadId) = UPPER(@uploadId)
ORDER BY CreatedAt ASC, Id ASC;";
            answerCmd.AddWithValue("@userId", userId);
            answerCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var reader = await answerCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var stepId = reader.GetString(0);
                var score = reader.GetDouble(4);
                var feedbackJson = reader.GetString(3);
                string? verdict = null;
                string? hint = null;
                try
                {
                    var feedback = System.Text.Json.JsonSerializer.Deserialize<TutorFeedback>(
                        feedbackJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    verdict = feedback?.Verdict;
                    hint = feedback?.Hint;
                }
                catch { }

                if (score >= 0.55)
                {
                    completed.Add(stepId);
                }
                else
                {
                    weakAttempts++;
                }

                answers.Add(new ReadingAnswerDataRecord(
                    stepId,
                    reader.GetString(1),
                    reader.GetString(2),
                    score,
                    verdict,
                    hint));
            }
        }

        var helpQuestions = new List<string>();
        await using (var helpCmd = conn.CreateCommand())
        {
            helpCmd.CommandText = @"
SELECT Question
FROM TutorHelpEvents
WHERE UserId = @userId
  AND UPPER(UploadId) = UPPER(@uploadId)
ORDER BY CreatedAt ASC, Id ASC;";
            helpCmd.AddWithValue("@userId", userId);
            helpCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var reader = await helpCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                helpQuestions.Add(reader.GetString(0));
            }
        }

        return new ReadingPerformanceDataRecord(
            categoryText,
            completed.Count,
            0,
            answers.Count,
            weakAttempts,
            helpQuestions.Count,
            answers,
            helpQuestions);
    }

    public async Task<List<TutorProgressRecord>> ListClassProgressAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM Classes WHERE Id = @classId AND InstructorId = @me LIMIT 1";
            check.AddWithValue("@classId", classId);
            check.AddWithValue("@me", instructorId);
            if (await check.ExecuteScalarAsync(cancellationToken) is null)
            {
                return [];
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  cs.StudentId,
  COALESCE(u.FullName, '') AS FullName,
  COALESCE(u.Email, '') AS Email,
  cc.UploadId,
  COALESCE(up.OriginalFileName, '') AS FileName,
  COUNT(DISTINCT CASE WHEN ta.Score >= 0.55 THEN ta.StepId END) AS AnswersSubmitted,
  COUNT(ta.Id) AS AnswerAttempts,
  COALESCE(SUM(CASE WHEN ta.Score < 0.55 THEN 1 ELSE 0 END), 0) AS WeakAttempts,
  (
    SELECT COUNT(*)
    FROM TutorHelpEvents he
    WHERE he.UserId = cs.StudentId
      AND UPPER(he.UploadId) = UPPER(cc.UploadId)
  ) AS HelpRequests,
  COALESCE(AVG(ta.Score), 0) AS AverageScore,
  MAX(
    COALESCE(ta.CreatedAt, ''),
    COALESCE((
      SELECT MAX(he.CreatedAt)
      FROM TutorHelpEvents he
      WHERE he.UserId = cs.StudentId
        AND UPPER(he.UploadId) = UPPER(cc.UploadId)
    ), ''),
    COALESCE((
      SELECT MAX(ts.UpdatedAt)
      FROM TutorSessions ts
      WHERE ts.UserId = cs.StudentId
        AND UPPER(ts.UploadId) = UPPER(cc.UploadId)
        AND ts.Focus = 'reading_coach'
    ), '')
  ) AS LastActivity,
  (
    SELECT ts.SessionId
    FROM TutorSessions ts
    WHERE ts.UserId = cs.StudentId
      AND UPPER(ts.UploadId) = UPPER(cc.UploadId)
      AND ts.Focus = 'reading_coach'
    ORDER BY ts.UpdatedAt DESC
    LIMIT 1
  ) AS LatestTutorSessionId,
  (
    SELECT ts.CurrentNode
    FROM TutorSessions ts
    WHERE ts.UserId = cs.StudentId
      AND UPPER(ts.UploadId) = UPPER(cc.UploadId)
      AND ts.Focus = 'reading_coach'
    ORDER BY ts.UpdatedAt DESC
    LIMIT 1
  ) AS CurrentNode,
  (
    SELECT ts.Category
    FROM TutorSessions ts
    WHERE ts.UserId = cs.StudentId
      AND UPPER(ts.UploadId) = UPPER(cc.UploadId)
      AND ts.Focus = 'reading_coach'
    ORDER BY ts.UpdatedAt DESC
    LIMIT 1
  ) AS ReadingCategory,
  (
    SELECT ta2.StepId
    FROM TutorAnswers ta2
    WHERE ta2.UserId = cs.StudentId
      AND UPPER(ta2.UploadId) = UPPER(cc.UploadId)
      AND ta2.Score < 0.55
    ORDER BY ta2.CreatedAt DESC, ta2.Id DESC
    LIMIT 1
  ) AS LastWeakStep,
  (
    SELECT he.Question
    FROM TutorHelpEvents he
    WHERE he.UserId = cs.StudentId
      AND UPPER(he.UploadId) = UPPER(cc.UploadId)
    ORDER BY he.CreatedAt DESC, he.Id DESC
    LIMIT 1
  ) AS LastHelpQuestion
FROM ClassStudents cs
JOIN ClassCases cc ON cc.ClassId = cs.ClassId
LEFT JOIN Users u ON u.Id = cs.StudentId
LEFT JOIN Uploads up ON up.UploadId = cc.UploadId
LEFT JOIN TutorAnswers ta
  ON ta.UserId = cs.StudentId
 AND UPPER(ta.UploadId) = UPPER(cc.UploadId)
WHERE cs.ClassId = @classId
GROUP BY cs.StudentId, cc.UploadId
ORDER BY Email, FileName;";
        cmd.AddWithValue("@classId", classId);

        var rows = new List<TutorProgressRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var completed = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5));
            var answerAttempts = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6));
            var weakAttempts = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7));
            var helpRequests = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8));
            var latestTutorSessionId = reader.IsDBNull(11) ? null : reader.GetString(11);
            var currentNode = reader.IsDBNull(12) ? null : reader.GetString(12);
            var category = ResolveReadingCategory(reader.IsDBNull(13) ? null : reader.GetString(13));
            var lastWeakStep = reader.IsDBNull(14) ? null : reader.GetString(14);
            var lastHelpQuestion = reader.IsDBNull(15) ? null : reader.GetString(15);
            var currentStepId = ResolveCurrentReadingStepId(category, currentNode, completed);
            var status = ResolveProgressStatus(category, completed, answerAttempts, weakAttempts, helpRequests, currentNode);
            rows.Add(new TutorProgressRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                category.ToString(),
                completed,
                GuidedReadingTutor.GetSteps(category).Count,
                answerAttempts,
                weakAttempts,
                helpRequests,
                reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                latestTutorSessionId,
                status,
                currentStepId is null ? null : new { id = currentStepId, title = GetReadingStepTitle(category, currentStepId) },
                lastWeakStep is null ? null : new { id = lastWeakStep, title = GetReadingStepTitle(category, lastWeakStep) },
                lastHelpQuestion));
        }

        return rows;
    }

    public async Task<ClassReadingCoachSummaryRecord?> GetClassReadingCoachSummaryAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM Classes WHERE Id = @classId AND InstructorId = @me LIMIT 1";
            check.AddWithValue("@classId", classId);
            check.AddWithValue("@me", instructorId);
            if (await check.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  (SELECT COUNT(1) FROM ClassStudents WHERE ClassId = @classId) AS AssignedStudents,
  (SELECT COUNT(1) FROM ClassCases WHERE ClassId = @classId) AS AssignedCases,
  (
    SELECT COUNT(DISTINCT ts.UserId)
    FROM TutorSessions ts
    JOIN ClassStudents cs ON cs.StudentId = ts.UserId
    JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(ts.UploadId)
    WHERE cs.ClassId = @classId
      AND ts.Focus = 'reading_coach'
  ) AS StartedStudents,
  (
    SELECT COUNT(DISTINCT activity.StudentId)
    FROM (
      SELECT cs.StudentId
      FROM TutorSessions ts
      JOIN ClassStudents cs ON cs.StudentId = ts.UserId
      JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(ts.UploadId)
      WHERE cs.ClassId = @classId
        AND ts.Focus = 'reading_coach'
        AND ts.UpdatedAt >= @since
      UNION
      SELECT cs.StudentId
      FROM TutorHelpEvents he
      JOIN ClassStudents cs ON cs.StudentId = he.UserId
      JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(he.UploadId)
      WHERE cs.ClassId = @classId
        AND he.CreatedAt >= @since
      UNION
      SELECT cs.StudentId
      FROM TutorAnswers ta
      JOIN ClassStudents cs ON cs.StudentId = ta.UserId
      JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(ta.UploadId)
      WHERE cs.ClassId = @classId
        AND ta.CreatedAt >= @since
    ) activity
  ) AS ActiveStudents24h,
  (
    SELECT COUNT(1)
    FROM TutorHelpEvents he
    JOIN ClassStudents cs ON cs.StudentId = he.UserId
    JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(he.UploadId)
    WHERE cs.ClassId = @classId
      AND he.CreatedAt >= @since
  ) AS HelpRequests24h,
  (
    SELECT COUNT(1)
    FROM Messages m
    JOIN Sessions s ON s.Id = m.SessionId
    JOIN ClassStudents cs ON cs.StudentId = s.UserId
    JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(s.UploadId)
    WHERE cs.ClassId = @classId
      AND m.CreatedAt >= @since
  ) AS ChatMessages24h,
  (
    SELECT COUNT(1)
    FROM TutorAnswers ta
    JOIN ClassStudents cs ON cs.StudentId = ta.UserId
    JOIN ClassCases cc ON cc.ClassId = cs.ClassId AND UPPER(cc.UploadId) = UPPER(ta.UploadId)
    WHERE cs.ClassId = @classId
      AND ta.CreatedAt >= @since
  ) AS TutorAnswers24h;";
        cmd.AddWithValue("@classId", classId);
        cmd.AddWithValue("@since", DateTime.UtcNow.AddHours(-24).ToString("o"));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        int GetInt(int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        return new ClassReadingCoachSummaryRecord(
            classId,
            GetInt(0),
            GetInt(1),
            GetInt(2),
            GetInt(3),
            GetInt(4),
            GetInt(5),
            GetInt(6),
            DateTime.UtcNow.ToString("O"));
    }

    public async Task<TutorProgressDetailRecord?> GetTutorProgressDetailAsync(string classId, string instructorId, string studentId, Guid uploadId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var check = conn.CreateCommand())
        {
            check.CommandText = @"
SELECT 1
FROM Classes c
JOIN ClassStudents cs ON cs.ClassId = c.Id
JOIN ClassCases cc ON cc.ClassId = c.Id
WHERE c.Id = @classId
  AND c.InstructorId = @me
  AND cs.StudentId = @studentId
  AND UPPER(cc.UploadId) = UPPER(@uploadId)
LIMIT 1;";
            check.AddWithValue("@classId", classId);
            check.AddWithValue("@me", instructorId);
            check.AddWithValue("@studentId", studentId);
            check.AddWithValue("@uploadId", uploadId.ToString());

            if (await check.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        string? studentName = null;
        string? studentEmail = null;
        string? caseName = null;
        string? caseFileName = null;
        object? student = null;
        object? caseInfo = null;
        await using (var metaCmd = conn.CreateCommand())
        {
            metaCmd.CommandText = @"
SELECT
  COALESCE(u.FullName, ''),
  COALESCE(u.Email, ''),
  COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), ''),
  COALESCE(up.OriginalFileName, '')
FROM Users u
LEFT JOIN Uploads up ON UPPER(up.UploadId) = UPPER(@uploadId)
WHERE u.Id = @studentId
LIMIT 1;";
            metaCmd.AddWithValue("@studentId", studentId);
            metaCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var metaReader = await metaCmd.ExecuteReaderAsync(cancellationToken);
            if (await metaReader.ReadAsync(cancellationToken))
            {
                var fullName = metaReader.GetString(0);
                var email = metaReader.GetString(1);
                var resolvedCaseName = metaReader.GetString(2);
                var originalFileName = metaReader.GetString(3);

                studentName = string.IsNullOrWhiteSpace(fullName) ? null : fullName;
                studentEmail = string.IsNullOrWhiteSpace(email) ? null : email;
                caseName = string.IsNullOrWhiteSpace(resolvedCaseName) ? null : resolvedCaseName;
                caseFileName = string.IsNullOrWhiteSpace(originalFileName) ? null : originalFileName;

                student = new
                {
                    id = studentId,
                    fullName = studentName,
                    email = studentEmail
                };

                caseInfo = new
                {
                    uploadId,
                    name = caseName,
                    originalFileName = caseFileName
                };
            }
        }

        var category = DocType.AcademicResearch;
        var answers = new List<TutorProgressDetailAnswerRecord>();
        var completedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weakAttemptCount = 0;
        string? lastWeakStepId = null;
        await using (var answersCmd = conn.CreateCommand())
        {
            answersCmd.CommandText = @"
SELECT
  Id,
  SessionId,
  StepId,
  Question,
  Answer,
  Feedback,
  Score,
  CreatedAt
FROM TutorAnswers
WHERE UserId = @studentId
  AND UPPER(UploadId) = UPPER(@uploadId)
ORDER BY CreatedAt ASC, Id ASC;";
            answersCmd.AddWithValue("@studentId", studentId);
            answersCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var reader = await answersCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var stepId = reader.GetString(2);
                var score = reader.GetDouble(6);
                if (score >= 0.55)
                {
                    completedStepIds.Add(stepId);
                }

                if (score < 0.55)
                {
                    weakAttemptCount++;
                    lastWeakStepId = stepId;
                }

                answers.Add(new TutorProgressDetailAnswerRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    stepId,
                    GetReadingStepTitle(category, stepId),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    ParseFeedback(reader.GetString(5)),
                    score,
                    reader.GetString(7)));
            }
        }

        var helpEvents = new List<TutorProgressDetailHelpEventRecord>();
        string? lastHelpQuestion = null;
        await using (var helpCmd = conn.CreateCommand())
        {
            helpCmd.CommandText = @"
SELECT
  Id,
  ChatSessionId,
  TutorSessionId,
  StepId,
  Question,
  CreatedAt
FROM TutorHelpEvents
WHERE UserId = @studentId
  AND UPPER(UploadId) = UPPER(@uploadId)
ORDER BY CreatedAt ASC, Id ASC;";
            helpCmd.AddWithValue("@studentId", studentId);
            helpCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var reader = await helpCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lastHelpQuestion = reader.GetString(4);
                helpEvents.Add(new TutorProgressDetailHelpEventRecord(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        string? latestTutorSessionId = null;
        string? currentNode = null;
        await using (var sessionCmd = conn.CreateCommand())
        {
            sessionCmd.CommandText = @"
SELECT SessionId, CurrentNode, Category
FROM TutorSessions
WHERE UserId = @studentId
  AND UPPER(UploadId) = UPPER(@uploadId)
  AND Focus = 'reading_coach'
ORDER BY UpdatedAt DESC
LIMIT 1;";
            sessionCmd.AddWithValue("@studentId", studentId);
            sessionCmd.AddWithValue("@uploadId", uploadId.ToString());

            await using var reader = await sessionCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                latestTutorSessionId = reader.GetString(0);
                currentNode = reader.GetString(1);
                category = ResolveReadingCategory(reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        var currentStepId = ResolveCurrentReadingStepId(category, currentNode, completedStepIds.Count);
        var status = ResolveProgressStatus(category, completedStepIds.Count, answers.Count, weakAttemptCount, helpEvents.Count, currentNode);

        return new TutorProgressDetailRecord(
            classId,
            studentId,
            uploadId,
            student,
            caseInfo,
            studentName,
            studentEmail,
            caseName,
            caseFileName,
            category.ToString(),
            GuidedReadingTutor.GetSteps(category).Count,
            completedStepIds.Count,
            answers.Count,
            weakAttemptCount,
            helpEvents.Count,
            latestTutorSessionId,
            status,
            currentStepId is null ? null : new { id = currentStepId, title = GetReadingStepTitle(category, currentStepId) },
            lastWeakStepId is null ? null : new { id = lastWeakStepId, title = GetReadingStepTitle(category, lastWeakStepId) },
            lastHelpQuestion,
            status == "needs_help",
            answers,
            helpEvents);
    }

    private static DocType ResolveReadingCategory(string? categoryText)
    {
        return Enum.TryParse<DocType>(categoryText, out var category)
            ? category
            : DocType.AcademicResearch;
    }

    private static string? ResolveCurrentReadingStepId(DocType category, string? currentNode, int completedSteps)
    {
        if (string.IsNullOrWhiteSpace(currentNode))
        {
            return null;
        }

        if (currentNode.StartsWith("reading:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = currentNode.Split(':');
            return parts.Length >= 2 ? parts[1] : null;
        }

        return completedSteps > 0 ? GuidedReadingTutor.GetSteps(category).ElementAtOrDefault(completedSteps - 1)?.Id : null;
    }

    private static string ResolveProgressStatus(
        DocType category,
        int completedSteps,
        int answerAttempts,
        int weakAttempts,
        int helpRequests,
        string? currentNode)
    {
        if (string.Equals(currentNode, "reading:complete", StringComparison.OrdinalIgnoreCase) ||
            completedSteps >= GuidedReadingTutor.GetSteps(category).Count)
        {
            return "completed";
        }

        if (answerAttempts == 0 && string.IsNullOrWhiteSpace(currentNode))
        {
            return "not_started";
        }

        if (weakAttempts > 0 || helpRequests >= 2)
        {
            return "needs_help";
        }

        return "in_progress";
    }

    private static string GetReadingStepTitle(DocType category, string stepId)
    {
        return GuidedReadingTutor.GetSteps(category).FirstOrDefault(step => string.Equals(step.Id, stepId, StringComparison.OrdinalIgnoreCase))?.Title ?? stepId;
    }

    private static object? ParseFeedback(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<TutorFeedback>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

