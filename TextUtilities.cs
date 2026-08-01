using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class TextUtilityHelpers
{
    public static string SafeHead(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));
}

// ---------- Text normalization (applied at index time) ----------
public static class TextNormalization
{
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;

        // Common PDF ligatures
        s = s.Replace("?", "fi").Replace("?", "fl");

        // Join hyphenated line breaks: foo-\nbar -> foobar
        s = Regex.Replace(s, @"(\w)-\s*\r?\n\s*(\w)", "$1$2");

        // Normalize whitespace
        s = s.Replace('\u00A0', ' ');
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        s = Regex.Replace(s, @"\s+\r?\n", "\n");

        return s;
    }
}





public static class TextChunking
{


    public static IEnumerable<string> ChunkBySentences(string text, int maxChars = 1000, int overlap = 160)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        if (maxChars <= 0) yield break;
        if (overlap < 0) overlap = 0;

        // First, split into sentences properly
        var sentences = SplitIntoSentences(text);
        if (!sentences.Any()) yield break;

        var currentChunk = new System.Text.StringBuilder();
        var overlapText = "";

        foreach (var sentence in sentences)
        {
            // If adding this sentence would exceed max chars, yield current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + sentence.Length + 1 > maxChars)
            {
                var chunkText = currentChunk.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    yield return chunkText;

                    // Calculate overlap - take last N characters but try to start at a sentence
                    if (overlap > 0 && chunkText.Length > overlap / 2)
                    {
                        // Find a sentence boundary in the last part of the chunk for overlap
                        int overlapStart = Math.Max(0, chunkText.Length - overlap);

                        // Try to find a sentence start (capital letter after period)
                        for (int i = overlapStart; i < chunkText.Length - 1; i++)
                        {
                            if (chunkText[i] == '.' && i + 2 < chunkText.Length && char.IsUpper(chunkText[i + 2]))
                            {
                                overlapStart = i + 2;
                                break;
                            }
                        }

                        overlapText = chunkText.Substring(overlapStart);
                    }
                }

                // Start new chunk with overlap
                currentChunk.Clear();
                if (!string.IsNullOrWhiteSpace(overlapText))
                {
                    currentChunk.Append(overlapText);
                    currentChunk.Append(" ");
                }
            }

            // Add sentence to current chunk
            if (currentChunk.Length > 0) currentChunk.Append(" ");
            currentChunk.Append(sentence);
        }

        // Yield any remaining text
        var finalChunk = currentChunk.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(finalChunk) && finalChunk.Length > 50) // Avoid tiny chunks
        {
            yield return finalChunk;
        }
    }


    private static List<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var sentences = new List<string>();
        var currentSentence = new System.Text.StringBuilder();

        // Common abbreviations that don't end sentences
        var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dr", "Mr", "Mrs", "Ms", "Prof", "Ph.D", "M.D", "et al",
            "i.e", "e.g", "etc", "vs", "Fig", "Vol", "pp", "No"
        };

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            currentSentence.Append(c);

            // Check for sentence endings
            if (c == '.' || c == '!' || c == '?')
            {
                // Look ahead to see if this is really the end of a sentence
                bool isEnd = true;

                // Check if it's an abbreviation
                if (c == '.')
                {
                    // Get the word before the period
                    var beforePeriod = currentSentence.ToString().TrimEnd('.');
                    var lastWord = beforePeriod.Split(' ', '\n', '\t').LastOrDefault()?.Trim();

                    if (!string.IsNullOrEmpty(lastWord) && abbreviations.Contains(lastWord))
                    {
                        isEnd = false;
                    }

                    // Check for numbers (like 3.14)
                    if (i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
                    {
                        isEnd = false;
                    }

                    // Check if next character is lowercase (continuation)
                    if (i + 2 < text.Length && char.IsLower(text[i + 2]))
                    {
                        isEnd = false;
                    }
                }

                // If this is the end of a sentence and we have a space or newline next
                if (isEnd && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
                {
                    sentences.Add(currentSentence.ToString().Trim());
                    currentSentence.Clear();

                    // Skip whitespace
                    i++;
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    continue;
                }
            }

            i++;
        }

        // Add any remaining text as the last sentence
        if (currentSentence.Length > 0)
        {
            sentences.Add(currentSentence.ToString().Trim());
        }

        return sentences;
    }
}
