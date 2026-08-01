using System.Diagnostics;
using Api.Infrastructure;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenAI.Chat;
using System.Text.Json;

public record ChoiceSet(string c1, string c2, string c3);

public static class TutorChoiceWriter
{
    public static async Task<ChoiceSet> GenerateChoicesAsync(
        ChatClient chat,
        string focus,
        string narrative,
        List<string> chunkPreviews
    )
    {
        var context = string.Join("\n\n---\n\n", chunkPreviews.Take(3));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
    "You generate choices for a guided academic-paper tutor.\n\n" +

    "The choices must feel like clear lesson moves, not vague topic branches.\n" +
    "Each choice must use exactly two lines:\n" +
    "Line 1: a short action title, 3-7 words.\n" +
    "Line 2: a plain-language promise of what the learner will understand next, 8-16 words.\n\n" +

    "Good examples:\n" +
    "Examine the supporting evidence\nWalk through the results that support the paper's claim.\n" +
    "Compare quality and efficiency\nSee why the result depends on both performance and cost.\n" +
    "Test the limitation\nFind what the evidence does not fully prove.\n\n" +

    "Rules:\n" +
    "- Ground every choice in the document excerpts and current narrative.\n" +
    "- Make each choice specific to this paper, but not hardcoded to one known paper.\n" +
    "- Prefer verbs such as Examine, Compare, Trace, Test, Follow, Connect, Look at, Move to.\n" +
    "- Do not use arrows, ellipses, filler, or teaser phrases.\n" +
    "- Do not say 'A closer thread appears', 'another angle', 'wider map', or 'shift the view'.\n" +
    "- Do not expose internal labels such as interpret_metrics, result_relationships, childTarget, or drill.\n" +
    "- Avoid long academic wording and stacked abstractions.\n" +
    "- Do not include a navigation/back choice; the server adds those separately.\n\n" +

    "Return ONLY valid JSON in this format:\n" +
    "{ \"c1\": \"Title\\nPromise\", \"c2\": \"Title\\nPromise\", \"c3\": \"Title\\nPromise\" }"
),
            new UserChatMessage(
                $"FOCUS: {focus}\n\n" +
                $"NARRATIVE:\n{narrative}\n\n" +
                $"DOCUMENT EXCERPTS:\n{context}"
            )
        };

        var completionStarted = Stopwatch.GetTimestamp();
        var result = await Task.Run(() => chat.CompleteChat(messages));
        CasePilotTelemetry.RecordChatCompletion(
            result.Value,
            "tutor_choices",
            CasePilotTelemetry.ConfiguredAnswerModel,
            Stopwatch.GetElapsedTime(completionStarted));
        var text = string.Concat(result.Value.Content.Select(p => p.Text ?? "")).Trim();

        try
        {
            var parsed = JsonSerializer.Deserialize<ChoiceSet>(text);
            if (parsed != null) return parsed;
        }
        catch { }

        // fallback (very important)
        return new ChoiceSet(
            "Examine the supporting evidence\nWalk through the results that support this part of the paper.",
            "Connect the main ideas\nSee how this point fits into the paper's larger argument.",
            "Test the limitation\nLook at what the evidence does not fully settle."
        );
    }
}
