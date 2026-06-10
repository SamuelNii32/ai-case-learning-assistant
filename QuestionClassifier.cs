using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class QuestionClassifier
{
   public  static Task<QuestionType> ClassifyQuestionAsync(string question)
    {
        // Very short or empty → treat as Other
        if (string.IsNullOrWhiteSpace(question))
            return Task.FromResult(QuestionType.Other);

        // IMPROVED: Do simple pattern matching FIRST before calling the model
        var q = question.ToLowerInvariant();

        // Strong methodology signals
        if (Regex.IsMatch(q, @"\b(method(s|ology)?|approach(es)?|procedure|technique|experimental (setup|design|approach)|how (did|were).*?(conduct|perform|collect|measure|analyze))\b"))
            return Task.FromResult(QuestionType.Method);

        // Strong findings signals
        if (Regex.IsMatch(q, @"\b(finding(s)?|result(s)?|outcome(s)?|what (did|were).*?(find|discover|observe|show|demonstrate))\b"))
            return Task.FromResult(QuestionType.Findings);

        // Strong summary signals
        if (Regex.IsMatch(q, @"\b(summary|summarize|overview|about|main (point|idea)|key (point|takeaway)|abstract)\b"))
            return Task.FromResult(QuestionType.Summary);

        // Strong fact signals
        if (Regex.IsMatch(q, @"\b(who|when|where|which|what (is|are|was|were))\b"))
            return Task.FromResult(QuestionType.Fact);

        // Strong explanation signals
        if (Regex.IsMatch(q, @"\b(why|how|explain|rationale|reason)\b"))
            return Task.FromResult(QuestionType.WhyExplain);

        // If patterns didn't match, fall back to model classification
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

        var classifierModel = Environment.GetEnvironmentVariable("OPENAI_CLASSIFIER_MODEL")
            ?? "gpt-4o-mini"; // Use a better model

        var client = new OpenAI.Chat.ChatClient(classifierModel, apiKey);

        var messages = new List<OpenAI.Chat.ChatMessage>
    {
        new OpenAI.Chat.SystemChatMessage(
            "Classify this question about a research document. " +
            "Return ONLY ONE word: SUMMARY, FACT, METHOD, FINDINGS, WHY_EXPLAIN, or OTHER. " +
            "Nothing else."
        ),
        new OpenAI.Chat.UserChatMessage($"Question: {question}")
    };

        var options = new ChatCompletionOptions { Temperature = 0f };
        var result = client.CompleteChat(messages, options).Value;

        var raw = string.Concat(result.Content.Select(part => part.Text ?? string.Empty));
        var label = raw.Trim().ToUpperInvariant();

        return Task.FromResult(label switch
        {
            "SUMMARY" => QuestionType.Summary,
            "FACT" => QuestionType.Fact,
            "METHOD" => QuestionType.Method,
            "FINDINGS" => QuestionType.Findings,
            "WHY_EXPLAIN" => QuestionType.WhyExplain,
            _ => QuestionType.Other
        });
    }
}
