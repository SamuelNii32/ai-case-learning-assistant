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
    "You generate choice options for a guided academic tutor.\n\n" +

    "Each choice must:\n" +
    "- Be grounded in the document context\n" +
    "- Feel specific to the paper, not generic\n" +
    "- Be easy to scan quickly\n" +
    "- Feel like the next natural step in learning, not a random menu option\n" +
    "- Read like teaser-style continuations of the narrative\n" +
    "- Create subtle curiosity or tension without becoming vague\n" +
    "- Avoid command verbs like \"See\", \"Explore\", \"Understand\", \"Review\", \"Analyze\", \"Examine\", \"Check\", or \"Return\"\n" +
    "- Avoid sounding like an exam question\n" +
    "- Use at most 18 words before the arrow\n" +
    "- End with a short arrow phrase like → why it matters\n\n" +

    "Style:\n" +
    "- short, clear, curious\n" +
    "- statement fragments are allowed when they feel natural\n" +
    "- one sentence before the arrow\n" +
    "- no long multi-clause academic wording\n\n" +

    "Return ONLY valid JSON in this format:\n" +
    "{ \"c1\": \"...\", \"c2\": \"...\", \"c3\": \"...\" }"
),
            new UserChatMessage(
                $"FOCUS: {focus}\n\n" +
                $"NARRATIVE:\n{narrative}\n\n" +
                $"DOCUMENT EXCERPTS:\n{context}"
            )
        };

        var result = await Task.Run(() => chat.CompleteChat(messages));
        var text = string.Concat(result.Value.Content.Select(p => p.Text ?? "")).Trim();

        try
        {
            var parsed = JsonSerializer.Deserialize<ChoiceSet>(text);
            if (parsed != null) return parsed;
        }
        catch { }

        // fallback (very important)
        return new ChoiceSet(
            "The main claim rests on specific support.\n→ evidence behind the argument",
            "The ideas are connected more tightly than they first appear.\n→ how results fit together",
            "The argument has pressure points.\n→ limits and exceptions"
        );
    }
}
