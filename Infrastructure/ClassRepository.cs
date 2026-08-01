
using System.Data.Common;
using System.Text;

namespace Api.Infrastructure;

public sealed record CreatedClassRecord(
    string Id,
    string Name,
    string? Description,
    string JoinCode,
    string InstructorId,
    string CreatedAt);

public sealed record InstructorClassRecord(
    string Id,
    string Name,
    string? Description,
    string? JoinCode,
    string CreatedAt,
    int StudentCount,
    int CaseCount);

public sealed record JoinClassResult(
    bool UserFound,
    bool UserIsInstructor,
    bool ClassFound,
    string? ClassId,
    string? ClassName);

public sealed record EnrolledClassRecord(
    string Id,
    string Name,
    string? Description,
    string? JoinCode,
    string CreatedAt,
    string JoinedAt,
    string InstructorName,
    string InstructorEmail,
    List<EnrolledClassCaseRecord> Cases);

public sealed record EnrolledClassCaseRecord(
    string UploadId,
    string FileName,
    string? Objective,
    string? Focus,
    string? DueAt,
    string? ReadingCoachQuestions,
    string AssignedAt);

public sealed record ClassJoinCodeRecord(string ClassId, string ClassName, string JoinCode);

public sealed record AddStudentResult(bool ClassFound, bool StudentFound, bool StudentIsInstructor, string? StudentId, bool AlreadyInClass);

public sealed record RemoveStudentResult(bool ClassFound, bool Removed);

public sealed record AssignCaseResult(
    bool ClassFound,
    bool UploadFound,
    string? UploadId,
    string? Objective,
    string? Focus,
    string? DueAt,
    string? ReadingCoachQuestions,
    bool AlreadyAssigned,
    bool Updated);

public sealed record RemoveCaseResult(bool ClassFound, bool Removed);

public sealed record ClassDetailsStudentRecord(string Id, string Email, string? FullName);

public sealed record ClassDetailsCaseRecord(
    string UploadId,
    string FileName,
    string? Objective,
    string? Focus,
    string? DueAt,
    string? ReadingCoachQuestions,
    string AssignedAt);

public sealed record ClassDetailsRecord(
    string ClassId,
    string Name,
    string JoinCode,
    List<ClassDetailsStudentRecord> Students,
    List<ClassDetailsCaseRecord> Cases);

public sealed record ClassHistoryRecord(
    string SessionId,
    string StudentId,
    string StudentName,
    string StudentEmail,
    string? UploadId,
    string? CaseFileName,
    string StartedAt,
    int MessageCount,
    string? FirstUserQuestion);

public sealed record ClassStudentRecord(string StudentId, string FullName, string Email, string? AddedAt);

public sealed record ClassCaseRecord(string UploadId, string? FileName, string? ReadingCoachQuestions, string AssignedAt);

public sealed record InstructorSessionMessageRecord(string Role, string Content, string Timestamp);

public sealed record InstructorSessionLogRecord(
    string SessionId,
    string StudentId,
    string StudentName,
    string StudentEmail,
    string? UploadId,
    string CaseFileName,
    string CreatedAt,
    List<InstructorSessionMessageRecord> Messages);

public sealed record InstructorSessionLogResult(bool SessionFound, bool Authorized, InstructorSessionLogRecord? Log);

public sealed record ClassSessionMessageRecord(string Role, string Content, string CreatedAt);

public sealed record ClassSessionStudentRecord(string Id, string FullName, string Email);

public sealed record ClassSessionCaseInfoRecord(string? UploadId, string FileName);

public sealed record ClassSessionLogRecord(
    string SessionId,
    ClassSessionStudentRecord Student,
    ClassSessionCaseInfoRecord CaseInfo,
    string? StartedAt,
    List<ClassSessionMessageRecord> Messages);

public interface IClassRepository
{
    Task<CreatedClassRecord> CreateAsync(string instructorId, string name, string? description, CancellationToken cancellationToken = default);
    Task<List<InstructorClassRecord>> ListMineAsync(string instructorId, CancellationToken cancellationToken = default);
    Task<JoinClassResult> JoinByCodeAsync(string studentId, string joinCode, CancellationToken cancellationToken = default);
    Task<List<EnrolledClassRecord>> ListEnrolledAsync(string studentId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<ClassJoinCodeRecord?> GetOrCreateJoinCodeAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<ClassJoinCodeRecord?> RegenerateJoinCodeAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<AddStudentResult> AddStudentAsync(string classId, string instructorId, string studentEmail, CancellationToken cancellationToken = default);
    Task<RemoveStudentResult> RemoveStudentAsync(string classId, string instructorId, string studentId, CancellationToken cancellationToken = default);
    Task<AssignCaseResult> AssignCaseAsync(string classId, string instructorId, string uploadId, string? objective, string? focus, string? dueAt, string? readingCoachQuestions, CancellationToken cancellationToken = default);
    Task<RemoveCaseResult> RemoveCaseAsync(string classId, string instructorId, string uploadId, CancellationToken cancellationToken = default);
    Task<ClassDetailsRecord?> GetDetailsAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<PagedResult<ClassHistoryRecord>?> GetHistoryAsync(
        string classId,
        string instructorId,
        string? studentId,
        string? uploadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<List<ClassStudentRecord>?> ListStudentsAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<List<ClassCaseRecord>?> ListCasesAsync(string classId, string instructorId, CancellationToken cancellationToken = default);
    Task<InstructorSessionLogResult> GetInstructorSessionLogAsync(string sessionId, string instructorId, CancellationToken cancellationToken = default);
    Task<ClassSessionLogRecord?> GetClassSessionLogAsync(string classId, string sessionId, string instructorId, CancellationToken cancellationToken = default);
}

public sealed class SqliteClassRepository : IClassRepository
{
    private readonly DatabaseOptions _dbOptions;

    public SqliteClassRepository(IConfiguration configuration)
    {
        _dbOptions = DatabaseOptions.Load(configuration);
    }

    public async Task<CreatedClassRecord> CreateAsync(string instructorId, string name, string? description, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow.ToString("o");

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var joinCode = await GenerateUniqueJoinCodeAsync(conn, cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO Classes (Id, InstructorId, Name, Description, JoinCode, CreatedAt)
        VALUES (@id, @instructor, @name, @description, @joinCode, @createdAt);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@instructor", instructorId);
        cmd.AddWithValue("@name", name);
        cmd.AddWithValue("@description", (object?)description ?? DBNull.Value);
        cmd.AddWithValue("@joinCode", joinCode);
        cmd.AddWithValue("@createdAt", createdAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return new CreatedClassRecord(id, name, description, joinCode, instructorId, createdAt);
    }

    public async Task<List<InstructorClassRecord>> ListMineAsync(string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    c.Id,
    c.Name,
    c.Description,
    c.JoinCode,
    c.CreatedAt,
    (SELECT COUNT(1) FROM ClassStudents cs WHERE cs.ClassId = c.Id) AS StudentCount,
    (SELECT COUNT(1) FROM ClassCases cc WHERE cc.ClassId = c.Id) AS CaseCount
FROM Classes c
WHERE c.InstructorId = @instructorId
ORDER BY c.CreatedAt DESC;";
        cmd.AddWithValue("@instructorId", instructorId);

        var classes = new List<InstructorClassRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            classes.Add(new InstructorClassRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                Convert.ToInt32(reader.GetValue(5)),
                Convert.ToInt32(reader.GetValue(6))));
        }

        return classes;
    }

    public async Task<JoinClassResult> JoinByCodeAsync(string studentId, string joinCode, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var userCmd = conn.CreateCommand())
        {
            userCmd.CommandText = "SELECT COALESCE(IsSuperUser, 0) FROM Users WHERE Id = @userId LIMIT 1";
            userCmd.AddWithValue("@userId", studentId);
            var raw = await userCmd.ExecuteScalarAsync(cancellationToken);
            if (raw is null)
            {
                return new JoinClassResult(false, false, false, null, null);
            }

            if (Convert.ToInt32(raw) != 0)
            {
                return new JoinClassResult(true, true, false, null, null);
            }
        }

        string classId;
        string className;
        await using (var classCmd = conn.CreateCommand())
        {
            classCmd.CommandText = "SELECT Id, Name FROM Classes WHERE JoinCode = @joinCode LIMIT 1";
            classCmd.AddWithValue("@joinCode", joinCode);

            await using var reader = await classCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new JoinClassResult(true, false, false, null, null);
            }

            classId = reader.GetString(0);
            className = reader.GetString(1);
        }

        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
INSERT INTO ClassStudents (ClassId, StudentId, AddedAt)
VALUES (@classId, @studentId, @addedAt)
ON CONFLICT (ClassId, StudentId) DO NOTHING;";
            insert.AddWithValue("@classId", classId);
            insert.AddWithValue("@studentId", studentId);
            insert.AddWithValue("@addedAt", DateTime.UtcNow.ToString("o"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        return new JoinClassResult(true, false, true, classId, className);
    }

    public async Task<List<EnrolledClassRecord>> ListEnrolledAsync(string studentId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    c.Id,
    c.Name,
    c.Description,
    c.JoinCode,
    c.CreatedAt,
    cs.AddedAt,
    COALESCE(u.FullName, '') AS InstructorName,
    COALESCE(u.Email, '') AS InstructorEmail
FROM ClassStudents cs
JOIN Classes c ON c.Id = cs.ClassId
LEFT JOIN Users u ON u.Id = c.InstructorId
WHERE cs.StudentId = @studentId
ORDER BY cs.AddedAt DESC;";
        cmd.AddWithValue("@studentId", studentId);

        var classes = new List<EnrolledClassRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            classes.Add(new EnrolledClassRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                new List<EnrolledClassCaseRecord>()));
        }
        await reader.DisposeAsync();

        foreach (var cls in classes)
        {
            await using var caseCmd = conn.CreateCommand();
            caseCmd.CommandText = @"
SELECT
    up.UploadId,
    COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), up.UploadId) AS FileName,
    cc.Objective,
    cc.Focus,
    cc.DueAt,
    cc.ReadingCoachQuestions,
    cc.AssignedAt
FROM ClassCases cc
JOIN Uploads up ON UPPER(up.UploadId) = UPPER(cc.UploadId)
WHERE cc.ClassId = @classId
ORDER BY cc.AssignedAt DESC;";
            caseCmd.AddWithValue("@classId", cls.Id);

            await using var caseReader = await caseCmd.ExecuteReaderAsync(cancellationToken);
            while (await caseReader.ReadAsync(cancellationToken))
            {
                cls.Cases.Add(new EnrolledClassCaseRecord(
                    caseReader.GetString(0),
                    caseReader.GetString(1),
                    caseReader.IsDBNull(2) ? null : caseReader.GetString(2),
                    caseReader.IsDBNull(3) ? null : caseReader.GetString(3),
                    caseReader.IsDBNull(4) ? null : caseReader.GetString(4),
                    caseReader.IsDBNull(5) ? null : caseReader.GetString(5),
                    caseReader.GetString(6)));
            }
        }

        return classes;
    }

    public async Task<bool> DeleteAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();

        await using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT 1 FROM Classes WHERE Id = @classId AND InstructorId = @instructorId LIMIT 1";
            check.AddWithValue("@classId", classId);
            check.AddWithValue("@instructorId", instructorId);

            if (await check.ExecuteScalarAsync(cancellationToken) is null)
            {
                tx.Rollback();
                return false;
            }
        }

        foreach (var sql in new[]
        {
            "DELETE FROM ClassCases WHERE ClassId = @classId",
            "DELETE FROM ClassStudents WHERE ClassId = @classId",
            "DELETE FROM Classes WHERE Id = @classId AND InstructorId = @instructorId"
        })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.AddWithValue("@classId", classId);
            if (sql.Contains("@instructorId", StringComparison.Ordinal))
            {
                cmd.AddWithValue("@instructorId", instructorId);
            }
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
        return true;
    }

    public async Task<ClassJoinCodeRecord?> GetOrCreateJoinCodeAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var record = await LoadJoinCodeRecordAsync(conn, classId, instructorId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(record.JoinCode))
        {
            return record;
        }

        var joinCode = await GenerateUniqueJoinCodeAsync(conn, cancellationToken);
        await UpdateJoinCodeAsync(conn, classId, instructorId, joinCode, cancellationToken);
        return record with { JoinCode = joinCode };
    }

    public async Task<ClassJoinCodeRecord?> RegenerateJoinCodeAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var record = await LoadJoinCodeRecordAsync(conn, classId, instructorId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var joinCode = await GenerateUniqueJoinCodeAsync(conn, cancellationToken);
        await UpdateJoinCodeAsync(conn, classId, instructorId, joinCode, cancellationToken);
        return record with { JoinCode = joinCode };
    }

    public async Task<AddStudentResult> AddStudentAsync(string classId, string instructorId, string studentEmail, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return new AddStudentResult(false, false, false, null, false);
        }

        string? studentId;
        bool studentIsInstructor;
        await using (var findStudent = conn.CreateCommand())
        {
            findStudent.CommandText = @"
                SELECT Id, COALESCE(IsSuperUser, 0)
                FROM Users
                WHERE Email = @email;";
            findStudent.AddWithValue("@email", studentEmail.Trim().ToLowerInvariant());

            await using var studentReader = await findStudent.ExecuteReaderAsync(cancellationToken);
            if (!await studentReader.ReadAsync(cancellationToken))
            {
                return new AddStudentResult(true, false, false, null, false);
            }

            studentId = studentReader.GetString(0);
            studentIsInstructor = studentReader.GetInt32(1) != 0;
        }

        if (studentIsInstructor)
        {
            return new AddStudentResult(true, true, true, studentId, false);
        }

        await using (var checkExisting = conn.CreateCommand())
        {
            checkExisting.CommandText = @"
                SELECT COUNT(*)
                FROM ClassStudents
                WHERE ClassId = @classId AND StudentId = @studentId;";
            checkExisting.AddWithValue("@classId", classId);
            checkExisting.AddWithValue("@studentId", studentId);

            var exists = (long)(await checkExisting.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (exists > 0)
            {
                return new AddStudentResult(true, true, false, studentId, true);
            }
        }

        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
                INSERT INTO ClassStudents (ClassId, StudentId, AddedAt)
                VALUES (@classId, @studentId, @addedAt);";
            insert.AddWithValue("@classId", classId);
            insert.AddWithValue("@studentId", studentId);
            insert.AddWithValue("@addedAt", DateTime.UtcNow.ToString("o"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        return new AddStudentResult(true, true, false, studentId, false);
    }

    public async Task<RemoveStudentResult> RemoveStudentAsync(string classId, string instructorId, string studentId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return new RemoveStudentResult(false, false);
        }

        await using var delete = conn.CreateCommand();
        delete.CommandText = @"
            DELETE FROM ClassStudents
            WHERE ClassId = @classId AND StudentId = @studentId;";
        delete.AddWithValue("@classId", classId);
        delete.AddWithValue("@studentId", studentId);

        var removed = await delete.ExecuteNonQueryAsync(cancellationToken);
        return new RemoveStudentResult(true, removed > 0);
    }

    public async Task<AssignCaseResult> AssignCaseAsync(
        string classId,
        string instructorId,
        string uploadId,
        string? objective,
        string? focus,
        string? dueAt,
        string? readingCoachQuestions,
        CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return new AssignCaseResult(false, false, null, objective, focus, dueAt, readingCoachQuestions, false, false);
        }

        var canonicalUploadId = await FindInstructorUploadIdAsync(conn, uploadId.Trim(), instructorId, cancellationToken);
        if (canonicalUploadId is null)
        {
            return new AssignCaseResult(true, false, null, objective, focus, dueAt, readingCoachQuestions, false, false);
        }

        await using (var checkExisting = conn.CreateCommand())
        {
            checkExisting.CommandText = @"
                SELECT COUNT(*)
                FROM ClassCases
                WHERE ClassId = @classId AND UploadId = @uploadId;";
            checkExisting.AddWithValue("@classId", classId);
            checkExisting.AddWithValue("@uploadId", canonicalUploadId);

            var exists = (long)(await checkExisting.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (exists > 0)
            {
                await using var update = conn.CreateCommand();
                update.CommandText = @"
                    UPDATE ClassCases
                    SET Objective = @objective,
                        Focus = @focus,
                        DueAt = @dueAt,
                        ReadingCoachQuestions = @readingCoachQuestions
                    WHERE ClassId = @classId AND UploadId = @uploadId;";
                update.AddWithValue("@objective", (object?)objective ?? DBNull.Value);
                update.AddWithValue("@focus", (object?)focus ?? DBNull.Value);
                update.AddWithValue("@dueAt", (object?)dueAt ?? DBNull.Value);
                update.AddWithValue("@readingCoachQuestions", (object?)readingCoachQuestions ?? DBNull.Value);
                update.AddWithValue("@classId", classId);
                update.AddWithValue("@uploadId", canonicalUploadId);
                await update.ExecuteNonQueryAsync(cancellationToken);

                return new AssignCaseResult(true, true, canonicalUploadId, objective, focus, dueAt, readingCoachQuestions, true, true);
            }
        }

        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
                INSERT INTO ClassCases (ClassId, UploadId, Objective, Focus, DueAt, ReadingCoachQuestions, AssignedAt)
                VALUES (@classId, @uploadId, @objective, @focus, @dueAt, @readingCoachQuestions, @assignedAt);";
            insert.AddWithValue("@classId", classId);
            insert.AddWithValue("@uploadId", canonicalUploadId);
            insert.AddWithValue("@objective", (object?)objective ?? DBNull.Value);
            insert.AddWithValue("@focus", (object?)focus ?? DBNull.Value);
            insert.AddWithValue("@dueAt", (object?)dueAt ?? DBNull.Value);
            insert.AddWithValue("@readingCoachQuestions", (object?)readingCoachQuestions ?? DBNull.Value);
            insert.AddWithValue("@assignedAt", DateTime.UtcNow.ToString("o"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        return new AssignCaseResult(true, true, canonicalUploadId, objective, focus, dueAt, readingCoachQuestions, false, false);
    }

    public async Task<RemoveCaseResult> RemoveCaseAsync(string classId, string instructorId, string uploadId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return new RemoveCaseResult(false, false);
        }

        await using var delete = conn.CreateCommand();
        delete.CommandText = @"
            DELETE FROM ClassCases
            WHERE ClassId = @classId AND UPPER(UploadId) = UPPER(@uploadId);";
        delete.AddWithValue("@classId", classId);
        delete.AddWithValue("@uploadId", uploadId);

        var removed = await delete.ExecuteNonQueryAsync(cancellationToken);
        return new RemoveCaseResult(true, removed > 0);
    }

    public async Task<ClassDetailsRecord?> GetDetailsAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        var record = await LoadJoinCodeRecordAsync(conn, classId, instructorId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var joinCode = record.JoinCode;
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            joinCode = await GenerateUniqueJoinCodeAsync(conn, cancellationToken);
            await UpdateJoinCodeAsync(conn, classId, instructorId, joinCode, cancellationToken);
        }

        var students = new List<ClassDetailsStudentRecord>();
        await using (var stuCmd = conn.CreateCommand())
        {
            stuCmd.CommandText = @"
                SELECT Users.Id, Users.Email, Users.FullName
                FROM ClassStudents
                JOIN Users ON Users.Id = ClassStudents.StudentId
                WHERE ClassStudents.ClassId = @classId;";
            stuCmd.AddWithValue("@classId", classId);

            await using var reader = await stuCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                students.Add(new ClassDetailsStudentRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var cases = new List<ClassDetailsCaseRecord>();
        await using (var caseCmd = conn.CreateCommand())
        {
            caseCmd.CommandText = @"
                SELECT Uploads.UploadId,
                       Uploads.OriginalFileName,
                       ClassCases.Objective,
                       ClassCases.Focus,
                       ClassCases.DueAt,
                       ClassCases.ReadingCoachQuestions,
                       ClassCases.AssignedAt
                FROM ClassCases
                JOIN Uploads ON Uploads.UploadId = ClassCases.UploadId
                WHERE ClassCases.ClassId = @classId;";
            caseCmd.AddWithValue("@classId", classId);

            await using var reader = await caseCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cases.Add(new ClassDetailsCaseRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        return new ClassDetailsRecord(classId, record.ClassName, joinCode, students, cases);
    }

    public async Task<PagedResult<ClassHistoryRecord>?> GetHistoryAsync(
        string classId,
        string instructorId,
        string? studentId,
        string? uploadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize, 50);
        var offset = Pagination.Offset(page, pageSize);

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return null;
        }

        var sql = new StringBuilder(@"
SELECT
    s.Id                            AS SessionId,
    s.UserId                        AS UserId,
    COALESCE(u.FullName, '')          AS UserFullName,
    COALESCE(u.Email, '')             AS UserEmail,
    s.UploadId                      AS UploadId,
    COALESCE(up.OriginalFileName, '') AS OriginalFileName,
    s.CreatedAt                     AS SessionCreatedAt,
    (
        SELECT COUNT(1)
        FROM Messages m
        WHERE m.SessionId = s.Id
    ) AS MessageCount,
    (
        SELECT Content
        FROM Messages m
        WHERE m.SessionId = s.Id
          AND m.Role = 'user'
        ORDER BY m.CreatedAt ASC
        LIMIT 1
    ) AS FirstUserQuestion
FROM Sessions s
JOIN ClassStudents cs
    ON cs.StudentId = s.UserId
   AND cs.ClassId = @classId
JOIN ClassCases cc
    ON cc.ClassId = cs.ClassId
    AND UPPER(cc.UploadId) = UPPER(s.UploadId)
LEFT JOIN Users   u  ON u.Id        = s.UserId
LEFT JOIN Uploads up ON up.UploadId = s.UploadId
WHERE 1 = 1");

        var commandParams = new List<(string Name, object Value)>();
        commandParams.Add(("@classId", classId));

        if (!string.IsNullOrWhiteSpace(studentId))
        {
            sql.Append(" AND s.UserId = @studentId");
            commandParams.Add(("@studentId", studentId));
        }

        if (!string.IsNullOrWhiteSpace(uploadId))
        {
            sql.Append(" AND s.UploadId = @uploadId");
            commandParams.Add(("@uploadId", uploadId));
        }

        var baseSql = sql.ToString();
        var countSql = $"WITH base AS ({baseSql}) SELECT COUNT(1) FROM base;";
        var pageSql = $"WITH base AS ({baseSql}) SELECT * FROM base ORDER BY SessionCreatedAt DESC, SessionId DESC LIMIT @limit OFFSET @offset;";

        long totalCount;
        await using (var countSelect = conn.CreateCommand())
        {
            countSelect.CommandText = countSql;
            foreach (var (name, value) in commandParams)
            {
                countSelect.AddWithValue(name, value);
            }
            totalCount = Convert.ToInt64(await countSelect.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        var records = new List<ClassHistoryRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pageSql;
            foreach (var (name, value) in commandParams)
            {
                cmd.AddWithValue(name, value);
            }
            cmd.AddWithValue("@limit", pageSize);
            cmd.AddWithValue("@offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new ClassHistoryRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return new PagedResult<ClassHistoryRecord>(records, page, pageSize, (int)Math.Min(int.MaxValue, totalCount));
    }

    public async Task<List<ClassStudentRecord>?> ListStudentsAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return null;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                cs.StudentId,
                cs.AddedAt,
                u.FullName,
                u.Email
            FROM ClassStudents cs
            JOIN Users u ON u.Id = cs.StudentId
            WHERE cs.ClassId = @classId
            ORDER BY LOWER(COALESCE(u.FullName, '')) ASC, LOWER(u.Email) ASC;";
        cmd.AddWithValue("@classId", classId);

        var records = new List<ClassStudentRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ClassStudentRecord(
                reader.GetString(0),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return records;
    }

    public async Task<List<ClassCaseRecord>?> ListCasesAsync(string classId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return null;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                cc.UploadId,
                cc.AssignedAt,
                u.OriginalFileName,
                u.Name,
                cc.ReadingCoachQuestions
            FROM ClassCases cc
            JOIN Uploads u
                ON u.UploadId = cc.UploadId
            WHERE cc.ClassId = @classId
            ORDER BY cc.AssignedAt DESC;";
        cmd.AddWithValue("@classId", classId);

        var records = new List<ClassCaseRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var originalName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var shortName = reader.IsDBNull(3) ? null : reader.GetString(3);
            records.Add(new ClassCaseRecord(
                reader.GetString(0),
                string.IsNullOrWhiteSpace(shortName) ? originalName : shortName,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(1)));
        }

        return records;
    }

    public async Task<InstructorSessionLogResult> GetInstructorSessionLogAsync(string sessionId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string? studentId;
        string? uploadId;
        string createdAt;
        string studentName;
        string studentEmail;
        string caseFileName;

        await using (var sessionCmd = conn.CreateCommand())
        {
            sessionCmd.CommandText = @"
                SELECT s.UserId, s.UploadId, s.CreatedAt,
                       u.FullName, u.Email, up.OriginalFileName
                FROM Sessions s
                LEFT JOIN Users u ON u.Id = s.UserId
                LEFT JOIN Uploads up ON up.UploadId = s.UploadId
                WHERE s.Id = @sessionId;";
            sessionCmd.AddWithValue("@sessionId", sessionId);

            await using var reader = await sessionCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new InstructorSessionLogResult(false, false, null);
            }

            studentId = reader.GetString(0);
            uploadId = reader.IsDBNull(1) ? null : reader.GetString(1);
            createdAt = reader.GetString(2);
            studentName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            studentEmail = reader.IsDBNull(4) ? "" : reader.GetString(4);
            caseFileName = reader.IsDBNull(5) ? "" : reader.GetString(5);
        }

        if (!await InstructorCanAccessSessionAsync(conn, studentId, uploadId, instructorId, cancellationToken))
        {
            return new InstructorSessionLogResult(true, false, null);
        }

        var messages = new List<InstructorSessionMessageRecord>();
        await using (var msgCmd = conn.CreateCommand())
        {
            msgCmd.CommandText = @"
                SELECT Role, Content, CreatedAt
                FROM Messages
                WHERE SessionId = @sessionId
                ORDER BY CreatedAt ASC;";
            msgCmd.AddWithValue("@sessionId", sessionId);

            await using var reader = await msgCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new InstructorSessionMessageRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        var log = new InstructorSessionLogRecord(
            sessionId,
            studentId,
            studentName,
            studentEmail,
            uploadId,
            caseFileName,
            createdAt,
            messages);

        return new InstructorSessionLogResult(true, true, log);
    }

    public async Task<ClassSessionLogRecord?> GetClassSessionLogAsync(string classId, string sessionId, string instructorId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await ClassOwnedByInstructorAsync(conn, classId, instructorId, cancellationToken))
        {
            return null;
        }

        string userId;
        string? uploadId;
        string createdAt;

        await using (var sessionCmd = conn.CreateCommand())
        {
            sessionCmd.CommandText = @"
SELECT s.UserId, s.UploadId, s.CreatedAt
FROM Sessions s
JOIN ClassStudents cs
  ON cs.StudentId = s.UserId AND cs.ClassId = @classId
JOIN ClassCases cc
  ON cc.ClassId = cs.ClassId AND cc.UploadId = s.UploadId
WHERE s.Id = @sessionId;";
            sessionCmd.AddWithValue("@classId", classId);
            sessionCmd.AddWithValue("@sessionId", sessionId);

            await using var reader = await sessionCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            userId = reader.GetString(0);
            uploadId = reader.IsDBNull(1) ? null : reader.GetString(1);
            createdAt = reader.GetString(2);
        }

        var studentName = "";
        var studentEmail = "";
        await using (var stuCmd = conn.CreateCommand())
        {
            stuCmd.CommandText = @"
SELECT FullName, Email
FROM Users
WHERE Id = @uid;";
            stuCmd.AddWithValue("@uid", userId);

            await using var reader = await stuCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                studentName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                studentEmail = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
        }

        var fileName = "";
        if (!string.IsNullOrWhiteSpace(uploadId))
        {
            await using var fileCmd = conn.CreateCommand();
            fileCmd.CommandText = @"
SELECT OriginalFileName
FROM Uploads
WHERE UploadId = @up;";
            fileCmd.AddWithValue("@up", uploadId);

            await using var reader = await fileCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                fileName = reader.IsDBNull(0) ? "" : reader.GetString(0);
            }
        }

        var messages = new List<ClassSessionMessageRecord>();
        await using (var msgCmd = conn.CreateCommand())
        {
            msgCmd.CommandText = @"
SELECT Role, Content, CreatedAt
FROM Messages
WHERE SessionId = @sid
ORDER BY CreatedAt ASC;";
            msgCmd.AddWithValue("@sid", sessionId);

            await using var reader = await msgCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new ClassSessionMessageRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        return new ClassSessionLogRecord(
            sessionId,
            new ClassSessionStudentRecord(userId, studentName, studentEmail),
            new ClassSessionCaseInfoRecord(uploadId, fileName),
            createdAt,
            messages);
    }

    private static async Task<bool> ClassOwnedByInstructorAsync(DbConnection conn, string classId, string instructorId, CancellationToken cancellationToken)
    {
        await using var checkClass = conn.CreateCommand();
        checkClass.CommandText = @"
            SELECT 1
            FROM Classes
            WHERE Id = @classId AND InstructorId = @instructorId
            LIMIT 1;";
        checkClass.AddWithValue("@classId", classId);
        checkClass.AddWithValue("@instructorId", instructorId);

        return await checkClass.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> InstructorCanAccessSessionAsync(DbConnection conn, string studentId, string? uploadId, string instructorId, CancellationToken cancellationToken)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = @"
            SELECT 1
            FROM ClassStudents cs
            JOIN ClassCases cc ON cc.ClassId = cs.ClassId
            JOIN Classes c ON c.Id = cs.ClassId
            WHERE cs.StudentId = @studentId
              AND cc.UploadId = @uploadId
              AND c.InstructorId = @instructorId
            LIMIT 1;";
        checkCmd.AddWithValue("@studentId", studentId);
        checkCmd.AddWithValue("@uploadId", uploadId ?? "");
        checkCmd.AddWithValue("@instructorId", instructorId);

        return await checkCmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<string?> FindInstructorUploadIdAsync(DbConnection conn, string uploadId, string instructorId, CancellationToken cancellationToken)
    {
        await using var checkUpload = conn.CreateCommand();
        checkUpload.CommandText = @"
            SELECT UploadId
            FROM Uploads
            WHERE UPPER(UploadId) = UPPER(@uploadId) AND UserId = @ownerId
            LIMIT 1;";
        checkUpload.AddWithValue("@uploadId", uploadId);
        checkUpload.AddWithValue("@ownerId", instructorId);

        var result = await checkUpload.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : (string)result;
    }

    private static async Task<ClassJoinCodeRecord?> LoadJoinCodeRecordAsync(DbConnection conn, string classId, string instructorId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Name, JoinCode
            FROM Classes
            WHERE Id = @classId AND InstructorId = @instructorId;";
        cmd.AddWithValue("@classId", classId);
        cmd.AddWithValue("@instructorId", instructorId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClassJoinCodeRecord(
            classId,
            reader.GetString(0),
            reader.IsDBNull(1) ? "" : reader.GetString(1));
    }

    private static async Task UpdateJoinCodeAsync(DbConnection conn, string classId, string instructorId, string joinCode, CancellationToken cancellationToken)
    {
        await using var update = conn.CreateCommand();
        update.CommandText = @"
            UPDATE Classes
            SET JoinCode = @joinCode
            WHERE Id = @classId AND InstructorId = @instructorId;";
        update.AddWithValue("@joinCode", joinCode);
        update.AddWithValue("@classId", classId);
        update.AddWithValue("@instructorId", instructorId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> GenerateUniqueJoinCodeAsync(DbConnection conn, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = GenerateJoinCode();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Classes WHERE JoinCode = @code LIMIT 1";
            cmd.AddWithValue("@code", code);
            if (await cmd.ExecuteScalarAsync(cancellationToken) is null)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique class join code.");
    }

    private static string GenerateJoinCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return new string(chars);
    }
}

