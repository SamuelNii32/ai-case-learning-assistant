using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class TutorAiWriter
{
    public static async Task<string> GenerateNarrativeAsync(
        ChatClient chat,
        string focus,
        string topic,
        List<string> chunkPreviews,
        List<int> cites)
    {
        var context = string.Join("\n\n---\n\n", chunkPreviews.Take(3));
        var pages = string.Join(", ", cites);

        var systemPrompt = focus.EndsWith("_overview", StringComparison.OrdinalIgnoreCase)
            ? "You are an academic tutor guiding a reader through a paper step-by-step. " +
              "Stay tightly grounded in the provided excerpts. " +
              "Write a high-level lesson step, not a dense report. " +
              "Avoid sounding like the paper itself. Write as a guide introducing the section. " +
              "Focus on what the section is about, what its central idea is, and why it matters. " +
              "Do NOT overload the response with too many technical details, metrics, or numbers unless they are essential. " +
              "Use a neutral academic tone. " +
              "Do NOT use 'you' or 'your'. " +
              "Refer to 'the paper' or 'the study'. " +
              "Write exactly 2 short paragraphs. " +
"Each paragraph should express one clear idea only, in 2 sentences maximum. " +
"Keep sentences simple and easy to follow. " +
"The first paragraph should state the one idea this step teaches. " +
"The second paragraph should explain why that idea matters for reading the paper. " +
"Do not add a third paragraph unless the excerpt is too weak to explain the section in two paragraphs. " +
"Do not list extra supporting details unless they are essential to the section’s main point. " +
              "Each paragraph must end with a page citation like [p:X]. " +
              "If the content is weak, say: 'I can’t find that in the document.' [p:X]"
            : "You are a guided academic tutor helping a reader understand a specific paper step-by-step. " +
  "Stay tightly grounded in the provided excerpts. " +
  "Do NOT summarize broadly — teach one idea from these excerpts. " +

  "Make the explanation feel connected to the previous learning path. " +
  "Use a natural progression: start from the current focus, then reveal the deeper idea. " +
  "Do not make the step feel like an isolated answer. " +
  "Use phrases like 'This matters because', 'The deeper issue is', or 'This connects back to' when appropriate. " +
  "Each paragraph should move the reader one step deeper in understanding. " +
  "Avoid packing many metrics, terms, methods, or caveats into one screen. " +
  "If the excerpts contain many details, choose the one most important teaching point and save the rest for choices. " +

  "Use a neutral academic tone. " +
  "Do NOT use 'you' or 'your'. " +
  "Refer to 'the paper' or 'the study'. " +
  "Each paragraph must clearly connect to the topic and build understanding. " +
  "Write 2 concise paragraphs, not long essays. " +
  "Each paragraph must end with a page citation like [p:X]. " +
  "If the content is weak, say: 'I can’t find that in the document.' [p:X]";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(
                $"FOCUS: {focus}\n" +
                $"TOPIC: {topic}\n\n" +
                $"PAGES: {pages}\n\n" +
                $"DOCUMENT EXCERPTS:\n{context}"
            )
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.2f
        };

        var result = await Task.Run(() => chat.CompleteChat(messages, options));
        var text = string.Concat(result.Value.Content.Select(part => part.Text ?? string.Empty)).Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            var fallbackPage = cites.Count > 0 ? cites[0] : 1;
            return $"I can’t find that in the document. [p:{fallbackPage}]";
        }

        return text;
    }
}
