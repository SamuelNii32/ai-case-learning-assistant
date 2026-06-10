using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using OpenAI.Chat;
using Api.Extensions;

public static class TutorEndpoints
{
    public static void MapTutorEndpoints(this WebApplication app, string connString)
    {
        app.MapPost("/tutor/start/{uploadId:guid}", async (Guid uploadId, TutorStartRequest? request, HttpContext ctx, IWebHostEnvironment env) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using (var conn = new SqliteConnection(connString))
            {
                await conn.OpenAsync();

                using var chk = conn.CreateCommand();
                chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
                chk.Parameters.AddWithValue("$u", uploadId.ToString());
                chk.Parameters.AddWithValue("$me", me);

                var ok = await chk.ExecuteScalarAsync();
                if (ok is null)
                {
                    return Results.NotFound(new { error = "not found" });
                }
            }

            DocType category;
            if (DocTypePersistence.TryLoad(uploadId, env, out var docTypeResult) && docTypeResult is not null)
            {
                category = docTypeResult.DocType;
            }
            else
            {
                category = DocType.UnsupportedOther;
            }

            if (category is not DocType.AcademicResearch and not DocType.BusinessCase)
            {
                return Results.BadRequest(new
                {
                    error = "Guided Tutor is only available for academic research papers and business cases.",
                    docType = category.ToString()
                });
            }

            var focus = request?.Focus ?? "overview";
            var sessionId = Guid.NewGuid().ToString("N");

            var session = new TutorSession(
                SessionId: sessionId,
                UploadId: uploadId,
                Category: category,
                Focus: focus,
                CurrentNode: "start",
                VisitedTopics: new List<string>(),
                VisitedPages: new List<int>(),
                History: new List<string>(),
                LastStepSummary: null,
                DrillPath: new List<TutorDrillNode>(),
                PendingDrillChoices: new List<TutorDrillNode>()
            );

            TutorSessionStore.Sessions[sessionId] = session;

            TutorResponse response;

            if (category == DocType.AcademicResearch)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be an academic research paper and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the main findings, the methodology, the theoretical framing, or the key concepts used in the paper. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Start with key findings\nRead the paper's core results and central claim.", TutorAction.ExploreFocus, "findings"),
                        new("c2", "Inspect the methodology\nTrace how the evidence was produced and evaluated.", TutorAction.ExploreFocus, "methodology"),
                        new("c3", "Read the background\nUnderstand the problem, prior work, and research gap.", TutorAction.ExploreFocus, "background"),
                        new("c4", "Clarify key concepts\nUnpack the terms and ideas carrying the argument.", TutorAction.ExploreFocus, "concepts"),
                        new("c5", "Move to implications\nConsider what follows if the paper's claim holds.", TutorAction.ExploreFocus, "implications")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else if (category == DocType.BusinessCase)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be a business case and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the central problem, the available alternatives, the analysis, or the recommendation. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Examine the central problem.\nUnderstand the core challenge facing the decision-maker.", TutorAction.ExploreFocus, "problem"),
                        new("c2", "Review the alternatives.\nCompare the main options presented in the case.", TutorAction.ExploreFocus, "alternatives"),
                        new("c3", "Analyze the evidence.\nLook at the reasoning, data, and trade-offs in the case.", TutorAction.ExploreFocus, "analysis"),
                        new("c4", "Evaluate the recommendation.\nConsider the proposed course of action and its implications.", TutorAction.ExploreFocus, "recommendation")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else if (category == DocType.LegalCase)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be a legal case and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the facts, the legal issues, the applicable rules, or the court’s reasoning. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Review the facts.\nUnderstand the events and background of the dispute.", TutorAction.ExploreFocus, "facts"),
                        new("c2", "Identify the legal issues.\nClarify the main questions the case is addressing.", TutorAction.ExploreFocus, "issues"),
                        new("c3", "Examine the rules and authorities.\nSee what legal principles or precedents apply.", TutorAction.ExploreFocus, "rules"),
                        new("c4", "Analyze the court’s reasoning.\nUnderstand how the decision is justified.", TutorAction.ExploreFocus, "analysis")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document does not match a supported tutor category for guided analysis. [p:1]\n\n" +
                        "Tutor mode currently works best for academic research papers, business cases, and legal cases. [p:1]\n\n" +
                        "Use chat mode for free-form questions about this document. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Return to chat mode.\nAsk a direct question about the document instead.", TutorAction.ChangeFocus, "chat")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Unsupported tutor category"
                );
            }

            session = session with
            {
                VisitedPages = response.Cites.Distinct().OrderBy(x => x).ToList(),
                History = new List<string> { response.StepSummary },
                LastStepSummary = response.StepSummary
            };

            TutorSessionStore.Sessions[sessionId] = session;
            await TutorSessionPersistence.SaveAsync(connString, session, me);
            return Results.Json(response);
        });

        app.MapPost("/tutor/reading/start/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, ChatClient chat) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            if (!await CanAccessUploadAsync(connString, uploadId, me))
            {
                return Results.NotFound(new { error = "not found" });
            }

            DocType category;
            if (DocTypePersistence.TryLoad(uploadId, env, out var docTypeResult) && docTypeResult is not null)
            {
                category = docTypeResult.DocType;
            }
            else
            {
                category = DocType.UnsupportedOther;
            }

            EnsureIndexLoaded(uploadId, env);
            var assignment = await LoadReadingAssignmentContextAsync(connString, uploadId, me);

            var sessionId = Guid.NewGuid().ToString("N");
            var session = new TutorSession(
                SessionId: sessionId,
                UploadId: uploadId,
                Category: category,
                Focus: "reading_coach",
                CurrentNode: "reading:orientation",
                VisitedTopics: new List<string> { "orientation" },
                VisitedPages: new List<int>(),
                History: new List<string> { "Reading coach started" },
                LastStepSummary: "Reading coach started",
                DrillPath: new List<TutorDrillNode>(),
                PendingDrillChoices: new List<TutorDrillNode>()
            );

            TutorSessionStore.Sessions[sessionId] = session;

            var response = await GuidedReadingTutor.BuildStepAsync(session, GuidedReadingTutor.GetSteps(category)[0], chat, assignment);
            session = session with
            {
                VisitedPages = response.Cites.Distinct().OrderBy(x => x).ToList(),
                LastStepSummary = response.StepSummary,
                History = new List<string> { response.StepSummary }
            };

            TutorSessionStore.Sessions[sessionId] = session;
            await TutorSessionPersistence.SaveAsync(connString, session, me);

            return Results.Json(response);
        });

        app.MapGet("/tutor/reading/resume/{uploadId:guid}", async (Guid uploadId, HttpContext ctx, IWebHostEnvironment env, ChatClient chat) =>
        {
            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            if (!await CanAccessUploadAsync(connString, uploadId, me))
            {
                return Results.NotFound(new { error = "not found" });
            }

            var session = await TutorSessionPersistence.TryLoadLatestReadingAsync(connString, uploadId, me);
            if (session is null)
            {
                return Results.NotFound(new
                {
                    error = "No reading coach session found",
                    canStart = true
                });
            }

            TutorSessionStore.Sessions[session.SessionId] = session;

            EnsureIndexLoaded(uploadId, env);
            var assignment = await LoadReadingAssignmentContextAsync(connString, uploadId, me);

            if (string.Equals(session.CurrentNode, "reading:complete", StringComparison.OrdinalIgnoreCase))
            {
                var performance = await LoadReadingPerformanceSnapshotAsync(connString, session.UploadId, me);
                var recap = await GuidedReadingTutor.BuildFinalRecapAsync(session, chat, performance, assignment);
                return Results.Json(recap with
                {
                    Stage = "recap",
                    StepId = "final_recap"
                });
            }

            var stepId = ExtractReadingStepId(session.CurrentNode) ?? "orientation";
            if (!GuidedReadingTutor.TryGetStep(session.Category, stepId, out var step))
            {
                step = GuidedReadingTutor.GetSteps(session.Category)[0];
            }

            var response = await GuidedReadingTutor.BuildStepAsync(session, step, chat, assignment);
            return Results.Json(response with
            {
                Stage = session.CurrentNode.EndsWith(":retry", StringComparison.OrdinalIgnoreCase) ? "retry" : "check"
            });
        });

        app.MapPost("/tutor/reading/answer", async (TutorAnswerRequest request, HttpContext ctx, IWebHostEnvironment env, ChatClient chat) =>
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.SessionId) ||
                string.IsNullOrWhiteSpace(request.StepId))
            {
                return Results.BadRequest(new { error = "Invalid request" });
            }

            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            if (!TutorSessionStore.Sessions.TryGetValue(request.SessionId, out var session))
            {
                session = await TutorSessionPersistence.TryLoadAsync(connString, request.SessionId);
                if (session is null)
                {
                    return Results.NotFound(new { error = "Tutor session not found" });
                }

                TutorSessionStore.Sessions[session.SessionId] = session;
            }

            if (!await CanAccessUploadAsync(connString, session.UploadId, me))
            {
                return Results.Forbid();
            }

            if (!GuidedReadingTutor.TryGetStep(session.Category, request.StepId, out var step))
            {
                return Results.BadRequest(new { error = "Unknown reading step" });
            }

            EnsureIndexLoaded(session.UploadId, env);
            var assignment = await LoadReadingAssignmentContextAsync(connString, session.UploadId, me);

            var (previews, _) = GuidedReadingTutor.Retrieve(session.UploadId, step.Query);
            var feedback = await GuidedReadingTutor.GradeAnswerAsync(chat, step, request.Answer ?? "", previews);
            await GuidedReadingTutor.SaveAnswerAsync(connString, session, me, step, request.Answer ?? "", feedback);

            var nextStep = GuidedReadingTutor.GetNextStep(session.Category, step.Id);
            TutorResponse response;
            var history = new List<string>(session.History ?? new List<string>())
            {
                $"Answered {step.Title}"
            };

            if (feedback.Score < 0.55)
            {
                response = await GuidedReadingTutor.BuildRetryStepAsync(session, step, feedback, chat, assignment);
                session = session with
                {
                    CurrentNode = $"reading:{step.Id}:retry",
                    History = history.Concat(new[] { $"Retry {step.Title}" }).ToList(),
                    LastStepSummary = $"Reading coach retry: {step.Title}"
                };
            }
            else if (nextStep is null)
            {
                var performance = await LoadReadingPerformanceSnapshotAsync(connString, session.UploadId, me);
                response = await GuidedReadingTutor.BuildFinalRecapAsync(session, chat, performance, assignment);
                session = session with
                {
                    CurrentNode = "reading:complete",
                    VisitedTopics = (session.VisitedTopics ?? new List<string>()).Concat(new[] { "complete" }).Distinct().ToList(),
                    VisitedPages = (session.VisitedPages ?? new List<int>()).Concat(response.Cites).Distinct().OrderBy(x => x).ToList(),
                    History = history.Concat(new[] { response.StepSummary }).ToList(),
                    LastStepSummary = response.StepSummary
                };
            }
            else
            {
                response = await GuidedReadingTutor.BuildStepAsync(session, nextStep, chat, assignment);
                response = response with { Feedback = feedback };
                session = session with
                {
                    CurrentNode = $"reading:{nextStep.Id}",
                    VisitedTopics = (session.VisitedTopics ?? new List<string>()).Concat(new[] { nextStep.Id }).Distinct().ToList(),
                    VisitedPages = (session.VisitedPages ?? new List<int>()).Concat(response.Cites).Distinct().OrderBy(x => x).ToList(),
                    History = history.Concat(new[] { response.StepSummary }).ToList(),
                    LastStepSummary = response.StepSummary
                };
            }

            TutorSessionStore.Sessions[session.SessionId] = session;
            await TutorSessionPersistence.SaveAsync(connString, session, me);

            return Results.Json(response);
        });

        app.MapGet("/admin/classes/{classId}/tutor-progress", async (string classId, HttpContext ctx) =>
        {
            var role = ctx.User.FindFirst("role")?.Value;
            if (!string.Equals(role, "instructor", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using var conn = new SqliteConnection(connString);
            await conn.OpenAsync();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM Classes WHERE Id = $classId AND InstructorId = $me LIMIT 1";
                check.Parameters.AddWithValue("$classId", classId);
                check.Parameters.AddWithValue("$me", me);
                if (await check.ExecuteScalarAsync() is null)
                {
                    return Results.NotFound(new { error = "Class not found" });
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT
  cs.StudentId,
  IFNULL(u.FullName, '') AS FullName,
  IFNULL(u.Email, '') AS Email,
  cc.UploadId,
  IFNULL(up.OriginalFileName, '') AS FileName,
  COUNT(DISTINCT CASE WHEN ta.Score >= 0.55 THEN ta.StepId END) AS AnswersSubmitted,
  COUNT(ta.Id) AS AnswerAttempts,
  IFNULL(SUM(CASE WHEN ta.Score < 0.55 THEN 1 ELSE 0 END), 0) AS WeakAttempts,
  (
    SELECT COUNT(*)
    FROM TutorHelpEvents he
    WHERE he.UserId = cs.StudentId
      AND UPPER(he.UploadId) = UPPER(cc.UploadId)
  ) AS HelpRequests,
  IFNULL(AVG(ta.Score), 0) AS AverageScore,
  MAX(
    IFNULL(ta.CreatedAt, ''),
    IFNULL((
      SELECT MAX(he.CreatedAt)
      FROM TutorHelpEvents he
      WHERE he.UserId = cs.StudentId
        AND UPPER(he.UploadId) = UPPER(cc.UploadId)
    ), ''),
    IFNULL((
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
WHERE cs.ClassId = $classId
GROUP BY cs.StudentId, cc.UploadId
ORDER BY Email, FileName;
";
            cmd.Parameters.AddWithValue("$classId", classId);

            var rows = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var completed = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetInt64(5));
                var answerAttempts = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetInt64(6));
                var weakAttempts = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetInt64(7));
                var helpRequests = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetInt64(8));
                var latestTutorSessionId = reader.IsDBNull(11) ? null : reader.GetString(11);
                var currentNode = reader.IsDBNull(12) ? null : reader.GetString(12);
                var category = ResolveReadingCategory(reader.IsDBNull(13) ? null : reader.GetString(13));
                var lastWeakStep = reader.IsDBNull(14) ? null : reader.GetString(14);
                var lastHelpQuestion = reader.IsDBNull(15) ? null : reader.GetString(15);
                var currentStepId = ResolveCurrentReadingStepId(category, currentNode, completed);
                var status = ResolveProgressStatus(category, completed, answerAttempts, weakAttempts, helpRequests, currentNode);
                rows.Add(new
                {
                    studentId = reader.GetString(0),
                    studentName = reader.GetString(1),
                    studentEmail = reader.GetString(2),
                    uploadId = reader.GetString(3),
                    fileName = reader.GetString(4),
                    readingCategory = category.ToString(),
                    completedSteps = completed,
                    totalSteps = GuidedReadingTutor.GetSteps(category).Count,
                    answerAttempts,
                    weakAttempts,
                    helpRequests,
                    averageScore = reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                    lastActivity = reader.IsDBNull(10) ? null : reader.GetString(10),
                    latestTutorSessionId,
                    status,
                    currentStep = currentStepId is null ? null : new
                    {
                        id = currentStepId,
                        title = GetReadingStepTitle(category, currentStepId)
                    },
                    lastWeakStep = lastWeakStep is null ? null : new
                    {
                        id = lastWeakStep,
                        title = GetReadingStepTitle(category, lastWeakStep)
                    },
                    lastHelpQuestion,
                    needsAttention = status == "needs_help"
                });
            }

            return Results.Ok(rows);
        });

        app.MapGet("/admin/classes/{classId}/tutor-progress/{studentId}/{uploadId:guid}", async (string classId, string studentId, Guid uploadId, HttpContext ctx) =>
        {
            var role = ctx.User.FindFirst("role")?.Value;
            if (!string.Equals(role, "instructor", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using var conn = new SqliteConnection(connString);
            await conn.OpenAsync();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = @"
SELECT 1
FROM Classes c
JOIN ClassStudents cs ON cs.ClassId = c.Id
JOIN ClassCases cc ON cc.ClassId = c.Id
WHERE c.Id = $classId
  AND c.InstructorId = $me
  AND cs.StudentId = $studentId
  AND UPPER(cc.UploadId) = UPPER($uploadId)
LIMIT 1;
";
                check.Parameters.AddWithValue("$classId", classId);
                check.Parameters.AddWithValue("$me", me);
                check.Parameters.AddWithValue("$studentId", studentId);
                check.Parameters.AddWithValue("$uploadId", uploadId.ToString());
                if (await check.ExecuteScalarAsync() is null)
                {
                    return Results.NotFound(new { error = "Progress record not found" });
                }
            }

            string? studentName = null;
            string? studentEmail = null;
            string? caseName = null;
            string? caseFileName = null;
            object? student = null;
            object? caseInfo = null;
            using (var metaCmd = conn.CreateCommand())
            {
                metaCmd.CommandText = @"
SELECT
  IFNULL(u.FullName, ''),
  IFNULL(u.Email, ''),
  COALESCE(NULLIF(up.Name, ''), NULLIF(up.OriginalFileName, ''), ''),
  IFNULL(up.OriginalFileName, '')
FROM Users u
LEFT JOIN Uploads up ON UPPER(up.UploadId) = UPPER($uploadId)
WHERE u.Id = $studentId
LIMIT 1;
";
                metaCmd.Parameters.AddWithValue("$studentId", studentId);
                metaCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

                using var metaReader = await metaCmd.ExecuteReaderAsync();
                if (await metaReader.ReadAsync())
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
            var answers = new List<object>();
            var completedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var weakAttemptCount = 0;
            string? lastWeakStepId = null;
            using (var answersCmd = conn.CreateCommand())
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
WHERE UserId = $studentId
  AND UPPER(UploadId) = UPPER($uploadId)
ORDER BY CreatedAt ASC, Id ASC;
";
                answersCmd.Parameters.AddWithValue("$studentId", studentId);
                answersCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

                using var reader = await answersCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
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

                    answers.Add(new
                    {
                        id = reader.GetInt64(0),
                        sessionId = reader.GetString(1),
                        stepId,
                        stepTitle = GetReadingStepTitle(category, stepId),
                        question = reader.GetString(3),
                        answer = reader.GetString(4),
                        feedback = reader.GetString(5),
                        feedbackSummary = ParseFeedback(reader.GetString(5)),
                        score,
                        createdAt = reader.GetString(7)
                    });
                }
            }

            var helpEvents = new List<object>();
            string? lastHelpQuestion = null;
            using (var helpCmd = conn.CreateCommand())
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
WHERE UserId = $studentId
  AND UPPER(UploadId) = UPPER($uploadId)
ORDER BY CreatedAt ASC, Id ASC;
";
                helpCmd.Parameters.AddWithValue("$studentId", studentId);
                helpCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

                using var reader = await helpCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lastHelpQuestion = reader.GetString(4);
                    helpEvents.Add(new
                    {
                        id = reader.GetInt64(0),
                        chatSessionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                        tutorSessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        stepId = reader.IsDBNull(3) ? null : reader.GetString(3),
                        question = reader.GetString(4),
                        createdAt = reader.GetString(5)
                    });
                }
            }

            string? latestTutorSessionId = null;
            string? currentNode = null;
            using (var sessionCmd = conn.CreateCommand())
            {
                sessionCmd.CommandText = @"
SELECT SessionId, CurrentNode, Category
FROM TutorSessions
WHERE UserId = $studentId
  AND UPPER(UploadId) = UPPER($uploadId)
  AND Focus = 'reading_coach'
ORDER BY UpdatedAt DESC
LIMIT 1;
";
                sessionCmd.Parameters.AddWithValue("$studentId", studentId);
                sessionCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

                using var reader = await sessionCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    latestTutorSessionId = reader.GetString(0);
                    currentNode = reader.GetString(1);
                    category = ResolveReadingCategory(reader.IsDBNull(2) ? null : reader.GetString(2));
                }
            }

            var currentStepId = ResolveCurrentReadingStepId(category, currentNode, completedStepIds.Count);
            var status = ResolveProgressStatus(category, completedStepIds.Count, answers.Count, weakAttemptCount, helpEvents.Count, currentNode);

            return Results.Ok(new
            {
                classId,
                studentId,
                uploadId,
                student,
                caseInfo,
                studentName,
                studentEmail,
                caseName,
                caseFileName,
                readingCategory = category.ToString(),
                totalSteps = GuidedReadingTutor.GetSteps(category).Count,
                completedSteps = completedStepIds.Count,
                answerAttempts = answers.Count,
                weakAttempts = weakAttemptCount,
                helpRequests = helpEvents.Count,
                latestTutorSessionId,
                status,
                currentStep = currentStepId is null ? null : new
                {
                    id = currentStepId,
                    title = GetReadingStepTitle(category, currentStepId)
                },
                lastWeakStep = lastWeakStepId is null ? null : new
                {
                    id = lastWeakStepId,
                    title = GetReadingStepTitle(category, lastWeakStepId)
                },
                lastHelpQuestion,
                needsAttention = status == "needs_help",
                answers,
                helpEvents
            });
        });

        app.MapPost("/tutor/step", async (TutorStepRequest request, HttpContext ctx, ChatClient chat) =>
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.SessionId) ||
                string.IsNullOrWhiteSpace(request.ChoiceId))
            {
                return Results.BadRequest(new { error = "Invalid request" });
            }

            if (!TutorSessionStore.Sessions.TryGetValue(request.SessionId, out var session))
            {
                session = await TutorSessionPersistence.TryLoadAsync(connString, request.SessionId);
                if (session is null)
                {
                    return Results.NotFound(new { error = "Tutor session not found" });
                }

                TutorSessionStore.Sessions[session.SessionId] = session;
            }

            var me = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using (var conn = new SqliteConnection(connString))
            {
                await conn.OpenAsync();

                using var chk = conn.CreateCommand();
                chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
                chk.Parameters.AddWithValue("$u", session.UploadId.ToString());
                chk.Parameters.AddWithValue("$me", me);

                var ok = await chk.ExecuteScalarAsync();
                if (ok is null)
                {
                    return Results.Forbid();
                }
            }

            TutorResponse response;

            // TOP-LEVEL ACADEMIC FOCUS HANDLING
            if (session.Category == DocType.AcademicResearch)
            {
                if (request.ChoiceId == "c1")
                {
                    session = session with
                    {
                        Focus = "findings",
                        CurrentNode = "focus:findings",
                        LastStepSummary = "Entered findings focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsOverview(session, chat);
                }

                else if (request.ChoiceId == "c1-1")
                {
                    session = session with
                    {
                        CurrentNode = "findings:supporting_evidence",
                        LastStepSummary = "Entered findings supporting evidence"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "supporting_evidence", chat);
                }
                else if (request.ChoiceId == "c1-2")
                {
                    session = session with
                    {
                        CurrentNode = "findings:why_it_matters",
                        LastStepSummary = "Entered findings why it matters"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "why_it_matters", chat);
                }
                else if (request.ChoiceId == "c1-3")
                {
                    session = session with
                    {
                        CurrentNode = "findings:limits_or_tradeoffs",
                        LastStepSummary = "Entered findings limits and trade-offs"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "limits_or_tradeoffs", chat);
                }
                else if (request.ChoiceId == "c1-4" || request.ChoiceId == "c1-1-c" || request.ChoiceId == "c1-2-c" || request.ChoiceId == "c1-3-c")
                {
                    session = session with
                    {
                        Focus = "findings",
                        CurrentNode = "focus:findings",
                        LastStepSummary = "Returned to findings focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on the paper’s main findings and how they support the overall argument. [p:1]\n\n" +
                            "The next step can examine how the findings are established, how the main results relate to one another, or whether the paper discusses exceptions, contrasts, or boundary cases. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1-1", "Examine the supporting evidence\nWalk through the results that support the main claim.", TutorAction.ExploreChildTopic, "supporting_evidence"),
                            new("c1-2", "Connect why it matters\nSee why the finding changes the paper's larger argument.", TutorAction.ExploreChildTopic, "why_it_matters"),
                            new("c1-3", "Test limits or trade-offs\nLook at what the finding does not fully settle.", TutorAction.ExploreChildTopic, "limits_or_tradeoffs"),
                            new("c1-4", "Change focus\nReturn to the main guide menu.", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to findings focus"
                    );
                }
                else if (request.ChoiceId == "c2")
                {
                    session = session with
                    {
                        Focus = "methodology",
                        CurrentNode = "focus:methodology",
                        LastStepSummary = "Entered methodology focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyOverview(session, chat);
                }
                else if (request.ChoiceId == "c3")
                {
                    session = session with
                    {
                        Focus = "background",
                        CurrentNode = "focus:background",
                        LastStepSummary = "Entered background focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundOverview(session, chat);
                }

                else if (request.ChoiceId == "c3-1")
                {
                    session = session with
                    {
                        CurrentNode = "background:problem_framing",
                        LastStepSummary = "Entered background problem framing"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "problem_framing", chat);
                }
                else if (request.ChoiceId == "c3-2")
                {
                    session = session with
                    {
                        CurrentNode = "background:prior_work",
                        LastStepSummary = "Entered background prior work"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "prior_work", chat);
                }
                else if (request.ChoiceId == "c3-3")
                {
                    session = session with
                    {
                        CurrentNode = "background:research_gap",
                        LastStepSummary = "Entered background research gap"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "research_gap", chat);
                }
                else if (request.ChoiceId == "c4")
                {
                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = "focus:concepts",
                        LastStepSummary = "Entered concepts focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicConceptsOverview(session, chat);
                }
                else if (request.ChoiceId == "c4-1" || request.ChoiceId == "c4-2" || request.ChoiceId == "c4-3")
                {
                    var target = request.ChoiceId switch
                    {
                        "c4-1" => "core_concept",
                        "c4-2" => "key_indicator",
                        "c4-3" => "concept_connections",
                        _ => "core_concept"
                    };

                    var drillTarget = request.ChoiceId switch
                    {
                        "c4-1" => "concept_definition",
                        "c4-2" => "concept_measurement",
                        "c4-3" => "concept_relationships",
                        _ => "concept_definition"
                    };

                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = $"concepts:{target}:{drillTarget}",
                        LastStepSummary = $"Entered concepts drill {drillTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    var drillNode = new TutorDrillNode(
                        Focus: "concepts",
                        ChildTarget: target,
                        DrillTarget: drillTarget,
                        Query: "",
                        Cites: new List<int>(),
                        Summary: "",
                        Depth: session.DrillPath?.Count ?? 0
                    );

                    response = await TutorRetrieval.BuildAcademicDrillResponse(session, drillNode, "concepts-back", chat);
                }
                else if (request.ChoiceId == "concepts-back")
                {
                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = "focus:concepts",
                        LastStepSummary = "Returned to concepts focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicConceptsOverview(session, chat);
                }
                else if (request.ChoiceId == "c5")
                {
                    session = session with
                    {
                        Focus = "implications",
                        CurrentNode = "focus:implications",
                        LastStepSummary = "Entered implications focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsOverview(session, chat);
                }

                else if (request.ChoiceId == "c5-1")
                {
                    session = session with
                    {
                        CurrentNode = "implications:broader_significance",
                        LastStepSummary = "Entered implications broader significance"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "broader_significance", chat);
                }
                else if (request.ChoiceId == "c5-2")
                {
                    session = session with
                    {
                        CurrentNode = "implications:practical_implications",
                        LastStepSummary = "Entered implications practical implications"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "practical_implications", chat);
                }
                else if (request.ChoiceId == "c5-3")
                {
                    session = session with
                    {
                        CurrentNode = "implications:limits_of_interpretation",
                        LastStepSummary = "Entered implications limits of interpretation"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "limits_of_interpretation", chat);
                }
                else if (request.ChoiceId == "c5-4" || request.ChoiceId == "c5-1-c" || request.ChoiceId == "c5-2-c" || request.ChoiceId == "c5-3-c")
                {
                    session = session with
                    {
                        Focus = "implications",
                        CurrentNode = "focus:implications",
                        LastStepSummary = "Returned to implications focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on what the paper’s conclusions mean beyond the immediate results. [p:1]\n\n" +
                            "The next step can examine the broader significance of the findings, the practical or policy implications, or the limitations that shape how far the conclusions should be taken. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c5-1", "Connect to the field\nSee what the paper contributes to the larger debate.", TutorAction.ExploreChildTopic, "broader_significance"),
                            new("c5-2", "Consider practical consequences\nLook at where the findings might matter beyond the paper.", TutorAction.ExploreChildTopic, "practical_implications"),
                            new("c5-3", "Test the interpretation\nSee what limits shape how far the conclusions go.", TutorAction.ExploreChildTopic, "limits_of_interpretation"),
                            new("c5-4", "Change focus\nReturn to the main guide menu.", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to implications focus"
                    );
                }

                else if (request.ChoiceId == "c2-1")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:data_sources",
                        LastStepSummary = "Entered methodology data sources"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "data_sources", chat);
                }
                else if (request.ChoiceId == "c2-2")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:measures",
                        LastStepSummary = "Entered methodology measures"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "measures", chat);
                }
                else if (request.ChoiceId == "c2-3")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:analysis_methods",
                        LastStepSummary = "Entered methodology analysis methods"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "analysis_methods", chat);
                }
                else if (request.ChoiceId == "c2-4" || request.ChoiceId == "c2-1-c" || request.ChoiceId == "c2-2-c" || request.ChoiceId == "c2-3-c")
                {
                    session = session with
                    {
                        Focus = "methodology",
                        CurrentNode = "focus:methodology",
                        LastStepSummary = "Returned to methodology focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on how the study was designed and how the evidence was produced. [p:1]\n\n" +
                            "The next step can examine the data or source material, the way key concepts are measured, or the analytical approach used to interpret the evidence. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c2-1", "Inspect the evidence base\nSee what data, sources, or materials set the scope.", TutorAction.ExploreChildTopic, "data_sources"),
                            new("c2-2", "Check the measures\nSee how the paper turns ideas into evidence.", TutorAction.ExploreChildTopic, "measures"),
                            new("c2-3", "Trace the analysis\nFollow the comparisons, models, or reasoning used.", TutorAction.ExploreChildTopic, "analysis_methods"),
                            new("c2-4", "Change focus\nReturn to the main guide menu.", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to methodology focus"
                    );
                }
                else if (request.ChoiceId == "drill:return" &&
                    TryResolveAcademicDrillReturn(session, out var returnFocus, out var returnChildTarget))
                {
                    session = session with
                    {
                        Focus = returnFocus,
                        CurrentNode = $"{returnFocus}:{returnChildTarget}",
                        LastStepSummary = $"Returned to {returnFocus} {returnChildTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await BuildAcademicDrillReturnResponse(session, returnFocus, returnChildTarget, chat);
                }
                else if (TryResolveAcademicDrillChoice(session, request.ChoiceId, out var drillNode, out var returnChoiceId))
                {
                    session = session with
                    {
                        Focus = drillNode.Focus,
                        CurrentNode = $"{drillNode.Focus}:{drillNode.ChildTarget}:{drillNode.DrillTarget}",
                        LastStepSummary = $"Entered {drillNode.Focus} drill {drillNode.DrillTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicDrillResponse(session, drillNode, returnChoiceId, chat);
                }
                else if (request.ChoiceId == "c3-4" || request.ChoiceId == "c4-4" || request.ChoiceId == "back-academic")
                {
                    session = session with
                    {
                        CurrentNode = "focus_menu",
                        LastStepSummary = "Returned to academic focus menu"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "The tutor has returned to the main focus menu for this academic paper. [p:1]\n\n" +
                            "Choose another direction to continue the guided analysis. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "Start with key findings\nRead the paper's core results and central claim.", TutorAction.ExploreFocus, "findings"),
                            new("c2", "Inspect the methodology\nTrace how the evidence was produced and evaluated.", TutorAction.ExploreFocus, "methodology"),
                            new("c3", "Read the background\nUnderstand the problem, prior work, and research gap.", TutorAction.ExploreFocus, "background"),
                            new("c4", "Clarify key concepts\nUnpack the terms and ideas carrying the argument.", TutorAction.ExploreFocus, "concepts"),
                            new("c5", "Move to implications\nConsider what follows if the paper's claim holds.", TutorAction.ExploreFocus, "implications")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to academic focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "That tutor choice is not available from the current academic branch. [p:1]\n\n" +
                            "Return to the academic focus menu and choose another supported direction. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("back-academic", "Change focus\nReturn to the main guide menu.", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Unsupported academic tutor choice"
                    );
                }
            }
            else if (session.Category == DocType.BusinessCase)
            {
                if (request.ChoiceId == "biz-back" || request.ChoiceId == "biz-recap")
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This document appears to be a business case and is suitable for guided analysis. [p:1]\n\n" +
                            "The tutor can begin with the central problem, the available alternatives, the analysis, or the recommendation. [p:1]\n\n" +
                            "Choose a direction to begin the exploration. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "Examine the central problem.\nUnderstand the core challenge facing the decision-maker.", TutorAction.ExploreFocus, "problem"),
                            new("c2", "Review the alternatives.\nCompare the main options presented in the case.", TutorAction.ExploreFocus, "alternatives"),
                            new("c3", "Analyze the evidence.\nLook at the reasoning, data, and trade-offs in the case.", TutorAction.ExploreFocus, "analysis"),
                            new("c4", "Evaluate the recommendation.\nConsider the proposed course of action and its implications.", TutorAction.ExploreFocus, "recommendation")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to business focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "Business case tutor branching has not been implemented yet, but category-aware start is working. [p:1]\n\n" +
                            "The next stage will add problem, alternatives, analysis, and recommendation flows. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("biz-back", "Return to the business focus menu.\nGo back to the top-level business case choices.", TutorAction.ChangeFocus, "focus_menu"),
                            new("biz-recap", "Trigger a recap.\nPreview how recap responses will work.", TutorAction.Recap, "recap")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Business case placeholder step"
                    );
                }
            }
            else if (session.Category == DocType.LegalCase)
            {
                if (request.ChoiceId == "legal-back" || request.ChoiceId == "legal-recap")
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This document appears to be a legal case and is suitable for guided analysis. [p:1]\n\n" +
                            "The tutor can begin with the facts, the legal issues, the applicable rules, or the court’s reasoning. [p:1]\n\n" +
                            "Choose a direction to begin the exploration. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "Review the facts.\nUnderstand the events and background of the dispute.", TutorAction.ExploreFocus, "facts"),
                            new("c2", "Identify the legal issues.\nClarify the main questions the case is addressing.", TutorAction.ExploreFocus, "issues"),
                            new("c3", "Examine the rules and authorities.\nSee what legal principles or precedents apply.", TutorAction.ExploreFocus, "rules"),
                            new("c4", "Analyze the court’s reasoning.\nUnderstand how the decision is justified.", TutorAction.ExploreFocus, "analysis")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to legal focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "Legal case tutor branching has not been implemented yet, but category-aware start is working. [p:1]\n\n" +
                            "The next stage will add facts, issues, rules, and analysis flows. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("legal-back", "Return to the legal focus menu.\nGo back to the top-level legal case choices.", TutorAction.ChangeFocus, "focus_menu"),
                            new("legal-recap", "Trigger a recap.\nPreview how recap responses will work.", TutorAction.Recap, "recap")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Legal case placeholder step"
                    );
                }
            }
            else
            {
                response = new TutorResponse(
                    SessionId: session.SessionId,
                    Narrative:
                        "Tutor mode is not available for this document category. [p:1]\n\n" +
                        "Use chat mode for direct questions instead. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("unsupported-chat", "Return to chat mode.\nAsk a direct question about the document instead.", TutorAction.ChangeFocus, "chat")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Unsupported tutor category"
                );
            }

            if (TutorSessionStore.Sessions.TryGetValue(session.SessionId, out var latestSession))
            {
                session = latestSession;
            }

            var isRecursiveDrill = IsAcademicRecursiveDrillChoice(request.ChoiceId);
            var isFirstLevelChildChoice = IsAcademicFirstLevelChildChoice(request.ChoiceId);
            var allowRecap =
                request.ChoiceId.Contains('-') &&
                !request.ChoiceId.EndsWith("-4", StringComparison.OrdinalIgnoreCase) &&
                !request.ChoiceId.EndsWith("-c", StringComparison.OrdinalIgnoreCase) &&
                !isFirstLevelChildChoice &&
                !isRecursiveDrill;

            if (session.Category == DocType.AcademicResearch && !isRecursiveDrill)
            {
                response = TutorRecap.FinalizeAcademicStep(session, response, allowRecap);
            }
            else if (session.Category != DocType.AcademicResearch)
            {
                TutorSessionStore.Sessions[session.SessionId] = session;
            }

            var sessionToPersist = TutorSessionStore.Sessions.TryGetValue(session.SessionId, out var cachedSession)
                ? cachedSession
                : session;
            await TutorSessionPersistence.SaveAsync(connString, sessionToPersist, me);

            return Results.Json(response);
        });
    }

    private static bool TryResolveAcademicDrillReturn(
        TutorSession session,
        out string focus,
        out string childTarget)
    {
        focus = "";
        childTarget = "";

        var path = session.DrillPath ?? new List<TutorDrillNode>();
        if (path.Count == 0)
        {
            return false;
        }

        var lastNode = path[^1];
        focus = lastNode.Focus;
        childTarget = lastNode.ChildTarget;

        return !string.IsNullOrWhiteSpace(childTarget) &&
            focus is "findings" or "methodology" or "background" or "concepts" or "implications";
    }

    private static async Task<TutorResponse> BuildAcademicDrillReturnResponse(
        TutorSession session,
        string focus,
        string childTarget,
        ChatClient chat)
    {
        return focus switch
        {
            "findings" => await TutorRetrieval.BuildAcademicFindingsResponse(session, childTarget, chat),
            "methodology" => await TutorRetrieval.BuildAcademicMethodologyResponse(session, childTarget, chat),
            "background" => await TutorRetrieval.BuildAcademicBackgroundResponse(session, childTarget, chat),
            "concepts" => await TutorRetrieval.BuildAcademicConceptsResponse(session, childTarget, chat),
            "implications" => await TutorRetrieval.BuildAcademicImplicationsResponse(session, childTarget, chat),
            _ => new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "That tutor drill return is not available from the current academic branch. [p:1]\n\n" +
                    "Return to the academic focus menu and choose another supported direction. [p:1]",
                Choices: new List<TutorChoice>
                {
                    new("back-academic", "Change focus\nReturn to the main guide menu.", TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Unsupported academic drill return"
            )
        };
    }

    private static bool TryResolveAcademicDrillChoice(
        TutorSession session,
        string choiceId,
        out TutorDrillNode drillNode,
        out string returnChoiceId)
    {
        drillNode = null!;
        returnChoiceId = "";

        if (choiceId.StartsWith("drill:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(choiceId["drill:".Length..], out var index))
            {
                var pending = session.PendingDrillChoices ?? new List<TutorDrillNode>();
                if (index >= 0 && index < pending.Count)
                {
                    drillNode = pending[index];
                    returnChoiceId = "drill:return";
                    return true;
                }
            }

            return false;
        }

        (var focus, var childTarget, var drillTarget, returnChoiceId) = choiceId switch
        {
            "c1-1-a" => ("findings", "supporting_evidence", "read_the_result", "c1-1-c"),
            "c1-1-b" => ("findings", "supporting_evidence", "connect_evidence_to_claim", "c1-1-c"),
            "c1-2-a" => ("findings", "why_it_matters", "argument_significance", "c1-2-c"),
            "c1-2-b" => ("findings", "why_it_matters", "field_or_practice_impact", "c1-2-c"),
            "c1-3-a" => ("findings", "limits_or_tradeoffs", "main_tradeoff", "c1-3-c"),
            "c1-3-b" => ("findings", "limits_or_tradeoffs", "boundary_condition", "c1-3-c"),

            "c2-1-a" => ("methodology", "data_sources", "dataset_breadth", "c2-1-c"),
            "c2-1-b" => ("methodology", "data_sources", "source_credibility", "c2-1-c"),
            "c2-2-a" => ("methodology", "measures", "variable_definition", "c2-2-c"),
            "c2-2-b" => ("methodology", "measures", "measurement_choices", "c2-2-c"),
            "c2-3-a" => ("methodology", "analysis_methods", "main_metrics", "c2-3-c"),
            "c2-3-b" => ("methodology", "analysis_methods", "comparison_strategy", "c2-3-c"),

            "c3-1-a" => ("background", "problem_framing", "problem_detail", "c3-1-c"),
            "c3-1-b" => ("background", "problem_framing", "why_problem_matters", "c3-1-c"),
            "c3-2-a" => ("background", "prior_work", "prior_work_detail", "c3-2-c"),
            "c3-2-b" => ("background", "prior_work", "prior_work_connection", "c3-2-c"),
            "c3-3-a" => ("background", "research_gap", "gap_detail", "c3-3-c"),
            "c3-3-b" => ("background", "research_gap", "contribution_positioning", "c3-3-c"),

            "c5-1-a" => ("implications", "broader_significance", "field_contribution", "c5-1-c"),
            "c5-1-b" => ("implications", "broader_significance", "significance_from_findings", "c5-1-c"),
            "c5-2-a" => ("implications", "practical_implications", "real_world_relevance", "c5-2-c"),
            "c5-2-b" => ("implications", "practical_implications", "policy_decision_impact", "c5-2-c"),
            "c5-3-a" => ("implications", "limits_of_interpretation", "main_constraints", "c5-3-c"),
            "c5-3-b" => ("implications", "limits_of_interpretation", "limits_from_method", "c5-3-c"),

            _ => (null!, null!, null!, null!)
        };

        if (focus is null)
        {
            return false;
        }

        drillNode = new TutorDrillNode(
            Focus: focus,
            ChildTarget: childTarget,
            DrillTarget: drillTarget,
            Query: "",
            Cites: new List<int>(),
            Summary: "",
            Depth: session.DrillPath?.Count ?? 0
        );

        return true;
    }

    private static bool IsAcademicRecursiveDrillChoice(string choiceId)
    {
        return choiceId.StartsWith("drill:", StringComparison.OrdinalIgnoreCase) ||
            choiceId is "c4-1" or "c4-2" or "c4-3" ||
            TryResolveAcademicDrillChoice(
                new TutorSession("", Guid.Empty, DocType.AcademicResearch, null, "", new List<string>(), new List<int>(), new List<string>(), null),
                choiceId,
                out _,
                out _);
    }

    private static bool IsAcademicFirstLevelChildChoice(string choiceId)
    {
        return choiceId is
            "c1-1" or "c1-2" or "c1-3" or
            "c2-1" or "c2-2" or "c2-3" or
            "c3-1" or "c3-2" or "c3-3" or
            "c5-1" or "c5-2" or "c5-3";
    }

    private static void EnsureIndexLoaded(Guid uploadId, IWebHostEnvironment env)
    {
        var id = uploadId.ToString();
        if (InMemoryStore.VectorIndex.TryGetValue(id, out var chunks) && chunks.Count > 0)
        {
            return;
        }

        IndexPersistence.TryLoad(uploadId, env, out _);
    }

    private static string? ExtractReadingStepId(string? currentNode)
    {
        if (string.IsNullOrWhiteSpace(currentNode))
        {
            return null;
        }

        if (string.Equals(currentNode, "reading:complete", StringComparison.OrdinalIgnoreCase))
        {
            return "final_recap";
        }

        var parts = currentNode.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "reading", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1];
    }

    private static string? ResolveCurrentReadingStepId(DocType category, string? currentNode, int completedSteps)
    {
        var fromNode = ExtractReadingStepId(currentNode);
        if (!string.IsNullOrWhiteSpace(fromNode))
        {
            return fromNode == "final_recap" ? null : fromNode;
        }

        var steps = GuidedReadingTutor.GetSteps(category);
        if (completedSteps >= steps.Count)
        {
            return null;
        }

        return steps[Math.Clamp(completedSteps, 0, steps.Count - 1)].Id;
    }

    private static string GetReadingStepTitle(DocType category, string stepId)
    {
        if (GuidedReadingTutor.TryGetStep(category, stepId, out var step) ||
            GuidedReadingTutor.TryGetStep(stepId, out step))
        {
            return step.Title;
        }

        return stepId;
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

    private static DocType ResolveReadingCategory(string? categoryText)
    {
        return Enum.TryParse<DocType>(categoryText, out var category)
            ? category
            : DocType.AcademicResearch;
    }

    private static async Task<ReadingAssignmentContext?> LoadReadingAssignmentContextAsync(
        string connString,
        Guid uploadId,
        string userId)
    {
        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT cc.Objective, cc.Focus, cc.DueAt
FROM ClassCases cc
JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
WHERE cs.StudentId = $userId
  AND UPPER(cc.UploadId) = UPPER($uploadId)
ORDER BY cc.AssignedAt DESC
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var objective = reader.IsDBNull(0) ? null : reader.GetString(0);
        var focus = reader.IsDBNull(1) ? null : reader.GetString(1);
        var dueAt = reader.IsDBNull(2) ? null : reader.GetString(2);

        return string.IsNullOrWhiteSpace(objective) &&
            string.IsNullOrWhiteSpace(focus) &&
            string.IsNullOrWhiteSpace(dueAt)
            ? null
            : new ReadingAssignmentContext(objective, focus, dueAt);
    }

    private static async Task<ReadingPerformanceSnapshot> LoadReadingPerformanceSnapshotAsync(
        string connString,
        Guid uploadId,
        string userId)
    {
        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        var category = DocType.AcademicResearch;
        using (var categoryCmd = conn.CreateCommand())
        {
            categoryCmd.CommandText = @"
SELECT Category
FROM TutorSessions
WHERE UserId = $userId
  AND UPPER(UploadId) = UPPER($uploadId)
  AND Focus = 'reading_coach'
ORDER BY UpdatedAt DESC
LIMIT 1;
";
            categoryCmd.Parameters.AddWithValue("$userId", userId);
            categoryCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());
            category = ResolveReadingCategory(await categoryCmd.ExecuteScalarAsync() as string);
        }

        var answers = new List<ReadingAnswerSnapshot>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weakAttempts = 0;

        using (var answerCmd = conn.CreateCommand())
        {
            answerCmd.CommandText = @"
SELECT StepId, Question, Answer, Feedback, Score
FROM TutorAnswers
WHERE UserId = $userId
  AND UPPER(UploadId) = UPPER($uploadId)
ORDER BY CreatedAt ASC, Id ASC;
";
            answerCmd.Parameters.AddWithValue("$userId", userId);
            answerCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

            using var reader = await answerCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stepId = reader.GetString(0);
                var score = reader.GetDouble(4);
                var feedback = ParseFeedback(reader.GetString(3));

                if (score >= 0.55)
                {
                    completed.Add(stepId);
                }
                else
                {
                    weakAttempts++;
                }

                answers.Add(new ReadingAnswerSnapshot(
                    StepId: stepId,
                    Question: reader.GetString(1),
                    Answer: reader.GetString(2),
                    Score: score,
                    Verdict: feedback?.Verdict,
                    Hint: feedback?.Hint
                ));
            }
        }

        var helpQuestions = new List<string>();
        using (var helpCmd = conn.CreateCommand())
        {
            helpCmd.CommandText = @"
SELECT Question
FROM TutorHelpEvents
WHERE UserId = $userId
  AND UPPER(UploadId) = UPPER($uploadId)
ORDER BY CreatedAt ASC, Id ASC;
";
            helpCmd.Parameters.AddWithValue("$userId", userId);
            helpCmd.Parameters.AddWithValue("$uploadId", uploadId.ToString());

            using var reader = await helpCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                helpQuestions.Add(reader.GetString(0));
            }
        }

        return new ReadingPerformanceSnapshot(
            CompletedSteps: completed.Count,
            TotalSteps: GuidedReadingTutor.GetSteps(category).Count,
            AnswerAttempts: answers.Count,
            WeakAttempts: weakAttempts,
            HelpRequests: helpQuestions.Count,
            Answers: answers,
            HelpQuestions: helpQuestions
        );
    }

    private static TutorFeedback? ParseFeedback(string json)
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

    private static async Task<bool> CanAccessUploadAsync(string connString, Guid uploadId, string userId)
    {
        using var conn = new SqliteConnection(connString);
        await conn.OpenAsync();

        using var chk = conn.CreateCommand();
        chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
        chk.Parameters.AddWithValue("$u", uploadId.ToString());
        chk.Parameters.AddWithValue("$me", userId);

        return await chk.ExecuteScalarAsync() is not null;
    }
}
