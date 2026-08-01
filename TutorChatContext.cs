using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Infrastructure;

public static class TutorChatContext
{
    public static async Task<string> BuildAsync(
        DatabaseOptions databaseOptions,
        string? tutorSessionId,
        string? tutorStepId,
        string userId)
    {
        TutorSession? session = null;
        if (!string.IsNullOrWhiteSpace(tutorSessionId))
        {
            session = await TutorSessionPersistence.TryLoadAsync(databaseOptions, tutorSessionId, userId);
        }

        var explicitStepProvided = !string.IsNullOrWhiteSpace(tutorStepId);
        var stepId = NormalizeStepId(tutorStepId);
        if (!explicitStepProvided && string.IsNullOrWhiteSpace(stepId) && session is not null)
        {
            stepId = StepIdFromCurrentNode(session.CurrentNode);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Tutor context:");
        sb.AppendLine("- The student is asking while using the guided tutor/reading coach.");
        sb.AppendLine("- Answer their question directly, but connect the explanation back to the active tutor step.");
        sb.AppendLine("- If the student is at a check question, help them understand the concept without drafting the checkpoint answer.");
        sb.AppendLine("- Prefer plain language, one concrete example, and a short next move.");

        if (session is not null)
        {
            if (!string.IsNullOrWhiteSpace(session.Focus))
            {
                sb.AppendLine($"- Tutor focus: {session.Focus}.");
            }

            if (!string.IsNullOrWhiteSpace(session.CurrentNode) && !explicitStepProvided)
            {
                sb.AppendLine($"- Current tutor node: {session.CurrentNode}.");
            }
            else if (!string.IsNullOrWhiteSpace(session.CurrentNode))
            {
                sb.AppendLine($"- Tutor session node for background only: {session.CurrentNode}.");
            }

            if (!string.IsNullOrWhiteSpace(session.LastStepSummary))
            {
                sb.AppendLine($"- Last tutor step: {session.LastStepSummary}.");
            }

            var recent = (session.History ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).TakeLast(5).ToList();
            if (recent.Count > 0)
            {
                sb.AppendLine($"- Recent tutor path: {string.Join(" -> ", recent)}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(stepId) && GuidedReadingTutor.TryGetStep(stepId, out var step))
        {
            sb.AppendLine($"- Reading Coach step: {step.Title}.");
            sb.AppendLine($"- Student check question: {step.Question}");
            sb.AppendLine("- The chat answer should give ingredients for the student's own answer, not a ready-made answer.");
        }

        return sb.ToString().Trim();
    }

    private static string? NormalizeStepId(string? stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
        {
            return null;
        }

        return stepId.Trim();
    }

    private static string? StepIdFromCurrentNode(string? currentNode)
    {
        if (string.IsNullOrWhiteSpace(currentNode))
        {
            return null;
        }

        var match = Regex.Match(currentNode, @"^reading:(?<step>[a-z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["step"].Value : null;
    }
}
