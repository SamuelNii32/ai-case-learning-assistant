using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Infrastructure;
using OpenAI.Chat;

public record GuidedReadingStepDef(
    string Id,
    string Title,
    string Query,
    string Question
);

public record ReadingAssignmentContext(
    string? Objective,
    string? Focus,
    string? DueAt,
    string? ReadingCoachQuestions
);

public record ReadingAnswerSnapshot(
    string StepId,
    string Question,
    string Answer,
    double Score,
    string? Verdict,
    string? Hint
);

public record ReadingPerformanceSnapshot(
    int CompletedSteps,
    int TotalSteps,
    int AnswerAttempts,
    int WeakAttempts,
    int HelpRequests,
    IReadOnlyList<ReadingAnswerSnapshot> Answers,
    IReadOnlyList<string> HelpQuestions
);

public static class GuidedReadingTutor
{
    private static readonly List<GuidedReadingStepDef> AcademicSteps = new()
    {
        new(
            "orientation",
            "Orientation",
            "abstract introduction contribution overview research problem main claim",
            "In one or two sentences, what is this document mainly trying to help the reader understand?"),
        new(
            "problem",
            "Problem",
            "problem challenge motivation why important introduction",
            "What problem, question, or difficulty is this document focused on?"),
        new(
            "research_gap",
            "Context Gap",
            "prior work related work gap limitation previous studies contribution",
            "What background, earlier work, or missing context makes this document necessary?"),
        new(
            "method",
            "Method",
            "method approach data experiment model design procedure",
            "What does the document do to study, explain, or solve the problem?"),
        new(
            "evidence",
            "Evidence",
            "result finding table figure evidence evaluation comparison performance",
            "What is one important piece of evidence or support the document uses?"),
        new(
            "limitations",
            "Limitations",
            "limitation caveat future work constraint threat validity discussion",
            "What is one thing the document does not fully prove, explain, or settle?")
    };

    private static readonly List<GuidedReadingStepDef> BusinessCaseSteps = new()
    {
        new(
            "situation",
            "Situation",
            "case situation company industry background context protagonist decision",
            "In one or two sentences, what is happening in this business case?"),
        new(
            "decision_problem",
            "Decision Problem",
            "decision problem challenge issue dilemma objective constraints urgency",
            "What decision or problem does the main decision-maker need to address?"),
        new(
            "stakeholders",
            "Stakeholders",
            "stakeholders customers competitors employees managers investors suppliers interests",
            "Who are the important stakeholders, and what do they care about?"),
        new(
            "options",
            "Options",
            "alternatives options choices strategy recommendation possible courses action",
            "What are the main options or courses of action available?"),
        new(
            "analysis",
            "Analysis",
            "analysis evidence data financial market operations tradeoff risk benefit",
            "What evidence or trade-offs should guide the decision?"),
        new(
            "recommendation",
            "Recommendation",
            "recommendation decision implementation action plan risk next steps",
            "What would you recommend, and why?")
    };

    public static IReadOnlyList<GuidedReadingStepDef> Steps => AcademicSteps;

    public static IReadOnlyList<GuidedReadingStepDef> GetSteps(DocType category) =>
        category == DocType.BusinessCase ? BusinessCaseSteps : AcademicSteps;

    public static bool TryGetStep(string stepId, out GuidedReadingStepDef step)
    {
        step = AcademicSteps
            .Concat(BusinessCaseSteps)
            .FirstOrDefault(x => string.Equals(x.Id, stepId, StringComparison.OrdinalIgnoreCase))!;
        return step is not null;
    }

    public static bool TryGetStep(DocType category, string stepId, out GuidedReadingStepDef step)
    {
        step = GetSteps(category).FirstOrDefault(x => string.Equals(x.Id, stepId, StringComparison.OrdinalIgnoreCase))!;
        return step is not null;
    }

    public static GuidedReadingStepDef? GetNextStep(string stepId)
    {
        return GetNextStep(DocType.AcademicResearch, stepId);
    }

    public static GuidedReadingStepDef? GetNextStep(DocType category, string stepId)
    {
        var steps = GetSteps(category);
        var index = steps.ToList().FindIndex(x => string.Equals(x.Id, stepId, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= steps.Count)
        {
            return null;
        }

        return steps[index + 1];
    }

    public static async Task<TutorResponse> BuildStepAsync(
        TutorSession session,
        GuidedReadingStepDef step,
        ChatClient chat,
        ReadingAssignmentContext? assignment = null)
    {
        var question = ResolveDisplayedQuestion(step, assignment);
        var (previews, cites) = Retrieve(session.UploadId, step.Query);
        var citationText = cites.Count == 0 ? "[p:1]" : string.Join("", cites.Take(3).Select(p => $"[p:{p}]"));

        var narrative = await GenerateStepNarrativeAsync(chat, step, previews, citationText, question, assignment);
        var steps = GetSteps(session.Category);
        var index = steps.ToList().FindIndex(x => string.Equals(x.Id, step.Id, StringComparison.OrdinalIgnoreCase));

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: new List<TutorChoice>(),
            Cites: cites.Count == 0 ? new List<int> { 1 } : cites,
            StepSummary: $"Reading coach: {step.Title}",
            Stage: "check",
            StepId: step.Id,
            Question: question,
            StepNumber: index + 1,
            TotalSteps: steps.Count
        );
    }

    public static async Task<TutorResponse> BuildRetryStepAsync(
        TutorSession session,
        GuidedReadingStepDef step,
        TutorFeedback feedback,
        ChatClient chat,
        ReadingAssignmentContext? assignment = null)
    {
        await Task.CompletedTask;

        var question = ResolveDisplayedQuestion(step, assignment);
        var (_, cites) = Retrieve(session.UploadId, step.Query);
        var steps = GetSteps(session.Category);
        var index = steps.ToList().FindIndex(x => string.Equals(x.Id, step.Id, StringComparison.OrdinalIgnoreCase));

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: "Let's slow this step down before moving on. Review the same passage and revise your answer using the feedback below.",
            Choices: new List<TutorChoice>(),
            Cites: cites.Count == 0 ? new List<int> { 1 } : cites,
            StepSummary: $"Reading coach retry: {step.Title}",
            Stage: "retry",
            StepId: step.Id,
            Question: question,
            StepNumber: index + 1,
            TotalSteps: steps.Count,
            Feedback: feedback
        );
    }

    public static async Task<TutorFeedback> GradeAnswerAsync(
        ChatClient chat,
        GuidedReadingStepDef step,
        string question,
        string answer,
        List<string> previews)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new TutorFeedback(0.1, "Write a short answer first.", "Use your own words. One sentence is enough to start.");
        }

        var context = string.Join("\n\n---\n\n", previews.Take(3));
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are grading a student's short answer about an assigned reading.\n" +
                "Be supportive but honest. Do not give a long lecture.\n" +
                "Do not spoon-feed the correct answer in the hint. The hint must coach revision without supplying the final wording.\n" +
                "If the answer is weak, point the student to the kind of evidence or concept to look for, then ask what they should revise.\n" +
                "Bad hint: \"State that the paper addresses the lack of large, diverse datasets for chart reasoning.\"\n" +
                "Good hint: \"Look back at the introduction. What gap in existing datasets or systems motivated the authors to build this work?\"\n" +
                "Return only valid JSON: {\"score\":0.0,\"verdict\":\"...\",\"hint\":\"...\"}\n" +
                "Score from 0 to 1. Verdict should be one sentence. Hint should be one concrete coaching question or revision direction."),
            new UserChatMessage(
                $"STEP: {step.Title}\nQUESTION: {question}\n\n" +
                $"DOCUMENT EXCERPTS:\n{context}\n\n" +
                $"STUDENT ANSWER:\n{answer}")
        };

        try
        {
            var result = await Task.Run(() => chat.CompleteChat(messages));
            var text = string.Concat(result.Value.Content.Select(p => p.Text ?? "")).Trim();
            var parsed = JsonSerializer.Deserialize<TutorFeedback>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is not null)
            {
                return parsed with
                {
                    Score = Math.Clamp(parsed.Score, 0, 1)
                };
            }
        }
        catch
        {
            // Use deterministic fallback below.
        }

        return BuildFallbackFeedback(answer);
    }

    public static async Task SaveAnswerAsync(
        DatabaseOptions databaseOptions,
        TutorSession session,
        string userId,
        GuidedReadingStepDef step,
        string question,
        string answer,
        TutorFeedback feedback)
    {
        await using var conn = databaseOptions.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO TutorAnswers (
  SessionId,
  UserId,
  UploadId,
  StepId,
  Question,
  Answer,
  Feedback,
  Score,
  CreatedAt
)
VALUES (
  $sessionId,
  $userId,
  $uploadId,
  $stepId,
  $question,
  $answer,
  $feedback,
  $score,
  $createdAt
);
";
cmd.AddWithValue("$sessionId", session.SessionId);
        cmd.AddWithValue("$userId", userId);
        cmd.AddWithValue("$uploadId", session.UploadId.ToString());
        cmd.AddWithValue("$stepId", step.Id);
        cmd.AddWithValue("$question", question);
        cmd.AddWithValue("$answer", answer);
        cmd.AddWithValue("$feedback", JsonSerializer.Serialize(feedback));
        cmd.AddWithValue("$score", feedback.Score);
        cmd.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<TutorResponse> BuildFinalRecapAsync(
        TutorSession session,
        ChatClient chat,
        ReadingPerformanceSnapshot? performance = null,
        ReadingAssignmentContext? assignment = null)
    {
        var (previews, cites) = Retrieve(session.UploadId, "abstract conclusion finding limitation contribution method");
        var citationText = cites.Count == 0 ? "[p:1]" : string.Join("", cites.Take(3).Select(p => $"[p:{p}]"));

        var context = string.Join("\n\n---\n\n", previews.Take(4));
        var assignmentText = BuildAssignmentPromptText(assignment);
        var performanceText = BuildPerformancePromptText(performance);
        var isBusinessCase = session.Category == DocType.BusinessCase;
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                (isBusinessCase
                    ? "You are a case-method coach helping a beginner finish a business case analysis path.\n"
                    : "You are a reading coach helping a beginner finish an academic research-paper reading path.\n") +
                "Write a concise final recap in plain language.\n" +
                "Use exactly four short sections with these headings: What you understood, What to review, " +
                (isBusinessCase ? "Case takeaway, Next move.\n" : "Paper takeaway, Next move.\n") +
                "Use the student's answer history when available. Do not shame the student.\n" +
                "If assignment instructions are provided, connect the recap to that objective.\n" +
                "Keep it under 180 words. Include the provided citation marker naturally."),
            new UserChatMessage(
                $"CITATION MARKER: {citationText}\n\n" +
                $"{assignmentText}" +
                $"{performanceText}" +
                $"DOCUMENT EXCERPTS:\n{context}")
        };

        var narrative = "You completed the guided reading path. " + citationText;
        try
        {
            var result = await Task.Run(() => chat.CompleteChat(messages));
            var text = string.Concat(result.Value.Content.Select(p => p.Text ?? "")).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                narrative = text;
            }
        }
        catch
        {
            // Keep fallback narrative.
        }

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: new List<TutorChoice>
            {
                new("c1", "Explore findings\nGo deeper into the document's results.", TutorAction.ChangeFocus, "findings"),
                new("c2", "Review methodology\nGo deeper into how the evidence was produced.", TutorAction.ChangeFocus, "methodology"),
                new("c3", "Review background\nGo deeper into the context gap.", TutorAction.ChangeFocus, "background")
            },
            Cites: cites.Count == 0 ? new List<int> { 1 } : cites,
            StepSummary: "Reading coach complete",
            Stage: "recap",
            StepId: "final_recap",
            StepNumber: GetSteps(session.Category).Count,
            TotalSteps: GetSteps(session.Category).Count
        );
    }

    private static string BuildPerformancePromptText(ReadingPerformanceSnapshot? performance)
    {
        if (performance is null)
        {
            return "";
        }

        var lines = new List<string>
        {
            "STUDENT PERFORMANCE:",
            $"Completed steps: {performance.CompletedSteps}/{performance.TotalSteps}",
            $"Answer attempts: {performance.AnswerAttempts}",
            $"Weak attempts: {performance.WeakAttempts}",
            $"Help requests: {performance.HelpRequests}"
        };

        foreach (var answer in performance.Answers.TakeLast(8))
        {
            lines.Add(
                $"- {answer.StepId}: score {answer.Score:0.##}; answer: {Truncate(answer.Answer, 180)}; feedback: {Truncate(answer.Verdict, 120)}");
        }

        foreach (var help in performance.HelpQuestions.TakeLast(5))
        {
            lines.Add($"- Help question: {Truncate(help, 160)}");
        }

        return string.Join("\n", lines) + "\n\n";
    }

    public static string ResolveDisplayedQuestion(GuidedReadingStepDef step, ReadingAssignmentContext? assignment = null)
    {
        var custom = assignment?.ReadingCoachQuestions?.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        return step.Question;
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = Regex.Replace(text.Trim(), "\\s+", " ");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
    }

    public static (List<string> Previews, List<int> Cites) Retrieve(Guid uploadId, string query)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(uploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return (new List<string>(), new List<int> { 1 });
        }

        var chosen = QaRetrieval.KeywordFallback(chunks, query, 5)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var previews = chosen.Select(x => x.Preview).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var cites = chosen.Select(x => x.Page).Distinct().OrderBy(x => x).ToList();

        if (cites.Count == 0)
        {
            cites.Add(1);
        }

        return (previews, cites);
    }

    private static async Task<string> GenerateStepNarrativeAsync(
        ChatClient chat,
        GuidedReadingStepDef step,
        List<string> previews,
        string citationText,
        string displayQuestion,
        ReadingAssignmentContext? assignment = null)
    {
        var assignmentText = BuildAssignmentPromptText(assignment);
        if (previews.Count == 0)
        {
            return
                $"This step is about {step.Title.ToLowerInvariant()}.\n\n" +
                $"I could not find enough indexed text for this document yet, so I cannot teach this milestone reliably. {citationText}\n\n" +
                "Takeaway\nIndex the document first so the coach can explain this step from evidence instead of guessing.";
        }

        var context = string.Join("\n\n---\n\n", previews.Take(4));
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                (step.Id is "situation" or "decision_problem" or "stakeholders" or "options" or "analysis" or "recommendation"
                    ? "You are a beginner-friendly business case-method coach.\n"
                    : "You are a beginner-friendly academic-paper reading coach.\n") +
                "Do not sound like a dense summary bot. Teach one reading milestone briefly.\n" +
                "Use this exact structure with short sections:\n" +
                "1. Start with one plain-language sentence explaining the milestone.\n" +
                "2. Add one short paragraph explaining only the most important idea for a beginner.\n" +
                "3. Add a section titled \"Example\" only if a simple analogy helps.\n" +
                "4. Add a section titled \"Document anchor\" with where this appears in the document.\n" +
                "5. Add a section titled \"Takeaway\" with one concise sentence.\n\n" +
                "Important rules:\n" +
                "- Do not include the check question. The app shows the question separately.\n" +
                "- Do not tell the student only to skim/read; explain the meaning too.\n" +
                "- Do not write a full mini-essay or cover every detail.\n" +
                "- Keep it under 170 words.\n" +
                "- If assignment instructions are provided, connect this milestone to that goal without forcing irrelevant details.\n" +
                "- Include the supplied citation marker naturally."),
            new UserChatMessage(
                $"READING STEP: {step.Title}\n" +
                $"CHECK QUESTION: {displayQuestion}\n" +
                $"CITATION MARKER: {citationText}\n\n" +
                $"{assignmentText}" +
                $"DOCUMENT EXCERPTS:\n{context}")
        };

        try
        {
            var result = await Task.Run(() => chat.CompleteChat(messages));
            var text = string.Concat(result.Value.Content.Select(p => p.Text ?? "")).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return EnsureCitation(text, citationText);
            }
        }
        catch
        {
            // Use fallback below.
        }

        var first = Regex.Replace(previews.FirstOrDefault() ?? "", @"\s+", " ").Trim();
        if (first.Length > 220)
        {
            first = first[..220].TrimEnd() + "...";
        }

        return
            $"This step is about {step.Title.ToLowerInvariant()}.\n\n" +
            $"The useful evidence here is: {first} {citationText}\n\n" +
            "Takeaway\nUse this evidence to answer the check in your own words.";
    }

    private static string BuildAssignmentPromptText(ReadingAssignmentContext? assignment)
    {
        if (assignment is null ||
            (string.IsNullOrWhiteSpace(assignment.Objective) &&
             string.IsNullOrWhiteSpace(assignment.Focus) &&
             string.IsNullOrWhiteSpace(assignment.DueAt) &&
             string.IsNullOrWhiteSpace(assignment.ReadingCoachQuestions)))
        {
            return "";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(assignment.Objective))
        {
            parts.Add($"Objective: {assignment.Objective}");
        }

        if (!string.IsNullOrWhiteSpace(assignment.Focus))
        {
            parts.Add($"Focus: {assignment.Focus}");
        }

        if (!string.IsNullOrWhiteSpace(assignment.DueAt))
        {
            parts.Add($"Due at: {assignment.DueAt}");
        }

        if (!string.IsNullOrWhiteSpace(assignment.ReadingCoachQuestions))
        {
            parts.Add("Custom Reading Coach questions:");
            parts.Add(assignment.ReadingCoachQuestions.Trim());
        }

        return "ASSIGNMENT INSTRUCTIONS:\n" + string.Join("\n", parts) + "\n\n";
    }

    private static string EnsureCitation(string text, string citationText)
    {
        return Regex.IsMatch(text, @"\[p:\d+\]", RegexOptions.IgnoreCase)
            ? text
            : $"{text} {citationText}";
    }

    private static TutorFeedback BuildFallbackFeedback(string answer)
    {
        var wordCount = Regex.Matches(answer, @"\b[\w'-]+\b").Count;
        if (wordCount < 8)
        {
            return new TutorFeedback(0.35, "This is a start, but it is too short to show understanding.", "Look for one concrete detail in the assigned section and use it to revise your answer in your own words.");
        }

        if (wordCount < 25)
        {
            return new TutorFeedback(0.65, "Good start. You have the basic idea, but it needs more evidence.", "Which specific method, result, example, or limitation in the document would make your answer more precise?");
        }

        return new TutorFeedback(0.8, "Solid answer. It shows you are connecting the document's point to evidence.", "What phrase or example from the document would make this claim more specific without making it longer?");
    }
}
