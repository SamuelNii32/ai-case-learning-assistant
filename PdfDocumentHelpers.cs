using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;


public static class PdfMetadataHelper
{
    public static (string? Title, string? Author) Read(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (null, null);
            using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
            var info = pdf.GetDocumentInfo();
            var title = info?.GetTitle();
            var author = info?.GetAuthor();
            return (string.IsNullOrWhiteSpace(title) ? null : title,
                    string.IsNullOrWhiteSpace(author) ? null : author);
        }
        catch { return (null, null); }
    }
}



public static class TitleHeuristics
{
    public static string? FromPdfFirstPage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        using var doc = PdfPigDoc.Open(path);
        var first = doc.GetPages().FirstOrDefault();
        if (first == null) return null;
        // Build cleaned candidate lines
        var lines = (first.Text ?? "")
            .Split('\n')
            .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
            .Where(s => s.Length >= 8)
            .ToList();

        // Skip obvious non-title headers
        var blacklist = new Regex(@"\b(UNIVERSITY|DEPARTMENT|SCHOOL|FACULTY|COLLEGE|INSTITUTE|SUBMITTED|SUBMISSION|SUPERVISOR|ADVIS(ER|OR)|\bBY\b|APPROVAL|DECLARATION|ACKNOWLEDG(E)?MENTS?|SIGNATURE|NAME OF|INDEX|CERTIFICATE)\b",
                                  RegexOptions.IgnoreCase);

        // Scoring function: uppercase-ish + reasonable length; penalize blacklisted & date-y lines
        float Score(string s)
        {
            int letters = s.Count(char.IsLetter);
            int upper = s.Count(char.IsUpper);
            float upperRatio = letters == 0 ? 0f : (float)upper / letters;

            float score = 0;
            score += upperRatio >= 0.60f ? 3 : (upperRatio >= 0.40f ? 1 : 0);
            int len = s.Length;
            score += (len >= 30 && len <= 160) ? 3 : (len >= 20 && len <= 180 ? 1 : -2);
            if (blacklist.IsMatch(s)) score -= 6;
            if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b")) score -= 1; // years
            return score;
        }

        // Rank candidates
        var ranked = lines
            .Select((s, i) => new { s, i, score = Score(s) })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.i) // prefer earlier on the page
            .ToList();

        string? pick = ranked.Count > 0 ? ranked[0].s : null;

        // If top candidate looks like it continues on the next line, join them
        if (pick != null)
        {
            int idx = ranked[0].i;
            if (idx + 1 < lines.Count)
            {
                string next = lines[idx + 1];
                int letters = next.Count(char.IsLetter);
                int upper = next.Count(char.IsUpper);
                float upperRatio = letters == 0 ? 0f : (float)upper / letters;

                if (!blacklist.IsMatch(next) && upperRatio >= 0.55f && (pick.Length + 1 + next.Length) <= 180)
                {
                    pick = $"{pick} {next}";
                }
            }
        }

        // --- post-process to trim header noise from the picked line ---

        if (string.IsNullOrWhiteSpace(pick)) return null;
        // 1) Cut anything after common separators (authors, supervisor, submission text)
        var cut = Regex.Split(pick, @"\b(BY|SUPERVISOR|SUBMITTED|SUBMISSION|NAME OF|SIGNATURE|APPROVAL)\b",
                              RegexOptions.IgnoreCase)[0];

        // 2) If there’s an institutional prelude, start from the first likely title keyword
        var m = Regex.Match(cut,
            @"\b(FINAL\s+YEAR|PROJECT\s+REPORT|THESIS|DISSERTATION|RESEARCH\s+PROJECT|REPORT\s+ON)\b",
            RegexOptions.IgnoreCase);
        if (m.Success)
            cut = cut.Substring(m.Index);

        // 3) Clean spacing
        cut = Regex.Replace(cut, @"\s{2,}", " ").Trim();

        // 4) Use trimmed value if it looks reasonable
        if (cut.Length >= 15 && cut.Length <= 200)
            pick = cut;

        // --- end post-process ---

        // 4b) If the candidate still looks more like a paragraph/abstract than a title, discard it
        if (!string.IsNullOrWhiteSpace(pick))
        {
            // Drop pure "ABSTRACT" lines
            if (Regex.IsMatch(pick, @"^\s*abstract\s*:?\s*$", RegexOptions.IgnoreCase))
                return null;

            // Rough word-count limit: most real titles are not 30+ words
            var words = pick.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 30)
                return null;

            // Rough multi-sentence check – abstracts usually have several sentences
            var sentenceEndCount = Regex.Matches(pick, "[\\.\\?!]").Count;
            if (sentenceEndCount > 2)
                return null;
        }

        return pick;





    }
}
