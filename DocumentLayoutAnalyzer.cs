using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using UglyToad.PdfPig.Content;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

public record LayoutManifest(
    Guid UploadId,
    DateTime GeneratedAt,
    List<LayoutCaption> Captions,
    List<LayoutTableCandidate> Tables,
    List<LayoutPageSummary> Pages);

public record LayoutCaption(
    string Id,
    string Kind,
    string Label,
    int Number,
    int Page,
    string Text,
    LayoutBox? BBox,
    double Confidence,
    List<string> Reasons);

public record LayoutTableCandidate(
    string Id,
    int Page,
    string Label,
    string TextPreview,
    LayoutBox? BBox,
    double Confidence,
    List<string> Reasons);

public record LayoutBox(double Left, double Top, double Width, double Height);

public record LayoutPageSummary(
    int Page,
    bool IsLikelyFrontMatter,
    int CaptionCandidateCount,
    int RasterImageCount,
    List<string> Reasons);

public static class DocumentLayoutAnalyzer
{
    private static readonly Regex CaptionStart = new(
        @"^\s*(?<kind>fig(?:ure)?|table)\s*\.?\s*(?<num>\d{1,3}|[ivxlcdm]{1,8})\s*[:\.\-\u2013\u2014]?\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TocLikeLine = new(
        @"\b(fig(?:ure)?|table)\s*\.?\s*(\d{1,3}|[ivxlcdm]{1,8})\b.{0,140}(\.{3,}|\s{4,})\s*\d{1,4}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EndsWithPageNumber = new(
        @"\s(?:\.{3,}|\s{4,})\s*\d{1,4}\s*$",
        RegexOptions.Compiled);

    public static LayoutManifest Analyze(Guid uploadId, IWebHostEnvironment env)
    {
        var pdfPath = Path.Combine(env.ContentRootPath, "uploads", $"{uploadId}.pdf");
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF not found", pdfPath);
        }

        var rasterCounts = PdfImageUtils.CountRasterImagesByPage(pdfPath);
        using var doc = PdfPigDoc.Open(pdfPath);

        var captions = new List<LayoutCaption>();
        var tables = new List<LayoutTableCandidate>();
        var pages = new List<LayoutPageSummary>();

        foreach (var page in doc.GetPages())
        {
            var lines = ExtractLines(page);
            var frontMatter = ClassifyFrontMatter(lines);
            var rasterCount = rasterCounts.TryGetValue(page.Number, out var count) ? count : 0;
            var candidates = FindCaptionCandidates(lines, page.Text ?? "");

            pages.Add(new LayoutPageSummary(
                Page: page.Number,
                IsLikelyFrontMatter: frontMatter.IsFrontMatter,
                CaptionCandidateCount: candidates.Count,
                RasterImageCount: rasterCount,
                Reasons: frontMatter.Reasons));

            foreach (var candidate in candidates)
            {
                var scored = ScoreCaption(candidate, lines, frontMatter.IsFrontMatter, rasterCount);
                if (scored.Score < 5)
                {
                    continue;
                }

                captions.Add(new LayoutCaption(
                    Id: $"{uploadId}-p{page.Number}-{candidate.Kind.ToLowerInvariant()}-{candidate.Number}",
                    Kind: candidate.Kind.Equals("table", StringComparison.OrdinalIgnoreCase) ? "table" : "figure",
                    Label: $"{NormalizeKind(candidate.Kind)} {candidate.Number}",
                    Number: candidate.NumberValue,
                    Page: page.Number,
                    Text: candidate.Text,
                    BBox: candidate.BBox,
                    Confidence: Math.Round(Math.Min(0.98, scored.Score / 10.0), 2),
                    Reasons: scored.Reasons));
            }

            tables.AddRange(FindTableCandidates(uploadId, page.Number, lines, frontMatter.IsFrontMatter));
        }

        return new LayoutManifest(uploadId, DateTime.UtcNow, captions, tables, pages);
    }

    public static async Task<LayoutManifest> AnalyzeAndSaveAsync(Guid uploadId, IWebHostEnvironment env)
    {
        var manifest = Analyze(uploadId, env);
        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsRoot);
        var path = Path.Combine(uploadsRoot, $"{uploadId}.layout.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        return manifest;
    }

    private static List<TextLine> ExtractLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Top)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<TextLine>();
        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            var line = lines.FirstOrDefault(l => Math.Abs(l.Bottom - y) <= 3.0);
            if (line is null)
            {
                lines.Add(new TextLine(new List<Word> { word }));
            }
            else
            {
                line.Words.Add(word);
            }
        }

        return lines
            .Select(l => l.Normalize())
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderByDescending(l => l.Top)
            .ThenBy(l => l.Left)
            .ToList();
    }

    private static FrontMatterResult ClassifyFrontMatter(List<TextLine> lines)
    {
        var reasons = new List<string>();
        var text = string.Join(" ", lines.Take(60).Select(l => l.Text));
        var captionHits = lines.Count(l => CaptionStart.IsMatch(l.Text));
        var tocHits = lines.Count(l => TocLikeLine.IsMatch(l.Text) || EndsWithPageNumber.IsMatch(l.Text));

        if (Regex.IsMatch(text, @"\b(table\s+of\s+contents|contents|list\s+of\s+figures|list\s+of\s+tables)\b", RegexOptions.IgnoreCase))
        {
            reasons.Add("front_matter_heading");
        }

        if (captionHits >= 5)
        {
            reasons.Add("many_caption_like_lines");
        }

        if (tocHits >= 3)
        {
            reasons.Add("toc_dot_leaders_or_page_numbers");
        }

        return new FrontMatterResult(reasons.Count > 0, reasons);
    }

    private static List<CaptionCandidate> FindCaptionCandidates(List<TextLine> lines, string rawPageText)
    {
        var results = new List<CaptionCandidate>();
        for (var i = 0; i < lines.Count; i++)
        {
            var match = CaptionStart.Match(lines[i].Text);
            if (!match.Success)
            {
                continue;
            }

            var kind = match.Groups["kind"].Value;
            var numberText = match.Groups["num"].Value;
            var caption = lines[i].Text;

            for (var j = i + 1; j < Math.Min(lines.Count, i + 3); j++)
            {
                var next = lines[j].Text;
                if (CaptionStart.IsMatch(next) || TocLikeLine.IsMatch(next))
                {
                    break;
                }

                if (next.Length is >= 20 and <= 180)
                {
                    caption += " " + next;
                }
            }

            results.Add(new CaptionCandidate(
                Kind: kind,
                Number: numberText,
                NumberValue: ParseNumber(numberText),
                Text: NormalizeCaptionText(caption),
                LineIndex: i,
                BBox: BuildBox(lines.Skip(i).Take(1))));
        }

        if (results.Count == 0 && !string.IsNullOrWhiteSpace(rawPageText))
        {
            foreach (Match match in Regex.Matches(
                rawPageText,
                @"(?<![A-Za-z])(?<kind>fig(?:ure)?|table)\s*\.?\s*(?<num>\d{1,3}|[ivxlcdm]{1,8})\s*[:\.\-\u2013\u2014]?\s*(?<rest>.{0,220})",
                RegexOptions.IgnoreCase))
            {
                var kind = match.Groups["kind"].Value;
                var numberText = match.Groups["num"].Value;
                var text = NormalizeCaptionText(match.Value);
                if (!LooksLikeUsableCaption(text))
                {
                    continue;
                }

                results.Add(new CaptionCandidate(
                    Kind: kind,
                    Number: numberText,
                    NumberValue: ParseNumber(numberText),
                    Text: text,
                    LineIndex: 0,
                    BBox: null));
            }
        }

        return results;
    }

    private static (int Score, List<string> Reasons) ScoreCaption(
        CaptionCandidate candidate,
        List<TextLine> lines,
        bool isFrontMatter,
        int rasterImageCount)
    {
        var score = 4;
        var reasons = new List<string> { "caption_pattern" };
        var line = lines[candidate.LineIndex].Text;

        if (isFrontMatter)
        {
            score -= 5;
            reasons.Add("front_matter_penalty");
        }

        if (TocLikeLine.IsMatch(line) || EndsWithPageNumber.IsMatch(line))
        {
            score -= 4;
            reasons.Add("toc_line_penalty");
        }

        var nearby = lines
            .Skip(Math.Max(0, candidate.LineIndex - 5))
            .Take(11)
            .ToList();

        if (nearby.Count(l => CaptionStart.IsMatch(l.Text)) >= 3)
        {
            score -= 3;
            reasons.Add("caption_cluster_penalty");
        }

        if (rasterImageCount > 0 && candidate.Kind.StartsWith("fig", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
            reasons.Add("page_has_raster_image");
        }

        if (candidate.Kind.Equals("table", StringComparison.OrdinalIgnoreCase) && nearby.Any(IsTableLikeLine))
        {
            score += 3;
            reasons.Add("near_table_like_text");
        }

        var words = candidate.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is >= 5 and <= 60)
        {
            score += 1;
            reasons.Add("caption_length_ok");
        }

        return (score, reasons);
    }

    private static List<LayoutTableCandidate> FindTableCandidates(Guid uploadId, int page, List<TextLine> lines, bool isFrontMatter)
    {
        if (isFrontMatter)
        {
            return new List<LayoutTableCandidate>();
        }

        var results = new List<LayoutTableCandidate>();
        var clusters = new List<List<TextLine>>();
        foreach (var line in lines)
        {
            if (!IsTableLikeLine(line))
            {
                continue;
            }

            var previous = clusters.LastOrDefault();
            if (previous is not null && Math.Abs(previous.Last().Bottom - line.Bottom) <= 18)
            {
                previous.Add(line);
            }
            else
            {
                clusters.Add(new List<TextLine> { line });
            }
        }

        var index = 1;
        foreach (var cluster in clusters.Where(c => c.Count >= 3))
        {
            var preview = Collapse(string.Join(" | ", cluster.Take(8).Select(l => l.Text)));
            results.Add(new LayoutTableCandidate(
                Id: $"{uploadId}-p{page}-table-candidate-{index++}",
                Page: page,
                Label: $"Table candidate {index - 1}",
                TextPreview: preview.Length > 600 ? preview[..600] : preview,
                BBox: BuildBox(cluster),
                Confidence: 0.55,
                Reasons: new List<string> { "repeated_numeric_or_columnar_lines" }));
        }

        return results;
    }

    private static bool IsTableLikeLine(TextLine line)
    {
        var text = line.Text;
        var numberCount = Regex.Matches(text, @"[-+]?\d+(?:\.\d+)?%?").Count;
        var multiSpaceColumns = Regex.IsMatch(text, @"\S+\s{2,}\S+\s{2,}\S+");
        var hasSeparators = text.Count(c => c == '|' || c == '\t') >= 2;
        return numberCount >= 3 || multiSpaceColumns || hasSeparators;
    }

    private static int ParseNumber(string raw)
    {
        if (int.TryParse(raw, out var n))
        {
            return n;
        }

        return 0;
    }

    private static string NormalizeKind(string kind)
    {
        return kind.StartsWith("fig", StringComparison.OrdinalIgnoreCase) ? "Figure" : "Table";
    }

    private static string NormalizeCaptionText(string text)
    {
        var clean = Collapse(text);
        clean = Regex.Replace(clean, @"\b(Fig(?:ure)?|Table)\s*\.?\s*(\d{1,3}|[ivxlcdm]{1,8})", m =>
        {
            var kind = NormalizeKind(m.Groups[1].Value);
            return $"{kind} {m.Groups[2].Value}";
        }, RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"(Figure|Table)\s+(\d{1,3}|[ivxlcdm]{1,8})\s*[:\.\-\u2013\u2014]?\s*", "$1 $2: ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"([a-z])([A-Z])", "$1 $2");
        clean = Regex.Replace(clean, @"([A-Za-z])[-\u2010-\u2015]([a-z])", "$1 $2");
        clean = TrimCaptionToLikelyBoundary(clean);
        return Collapse(clean);
    }

    private static string TrimCaptionToLikelyBoundary(string text)
    {
        var clean = Collapse(text);
        var label = Regex.Match(clean, @"^(Figure|Table)\s+\S+\s*:\s*", RegexOptions.IgnoreCase);
        if (!label.Success)
        {
            return clean.Length > 260 ? clean[..260] : clean;
        }

        var afterLabel = clean[label.Length..];
        if (afterLabel.Length > 120)
        {
            var firstSentence = Regex.Match(afterLabel, @"^.{12,180}?[\.!?](?=\s|[A-Z]|$)");
            if (firstSentence.Success)
            {
                return clean[..label.Length] + firstSentence.Value;
            }
        }

        return clean.Length > 260 ? clean[..260] : clean;
    }

    private static bool LooksLikeUsableCaption(string text)
    {
        if (text.Length < 12)
        {
            return false;
        }

        var afterLabel = Regex.Replace(text, @"^(Figure|Table)\s+\S+\s*:\s*", "", RegexOptions.IgnoreCase).Trim();
        if (afterLabel.Length < 8)
        {
            return false;
        }

        if (Regex.IsMatch(afterLabel, @"^[\)\]\},;:\.0-9\s]+"))
        {
            return false;
        }

        return Regex.IsMatch(afterLabel, @"[A-Za-z]{4,}");
    }

    private static string Collapse(string text)
    {
        return Regex.Replace(text ?? "", @"\s+", " ").Trim();
    }

    private static LayoutBox? BuildBox(IEnumerable<TextLine> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var left = list.Min(l => l.Left);
        var right = list.Max(l => l.Right);
        var top = list.Max(l => l.Top);
        var bottom = list.Min(l => l.Bottom);

        return new LayoutBox(
            Left: Math.Round(left, 2),
            Top: Math.Round(top, 2),
            Width: Math.Round(Math.Max(0, right - left), 2),
            Height: Math.Round(Math.Max(0, top - bottom), 2));
    }

    private sealed record CaptionCandidate(string Kind, string Number, int NumberValue, string Text, int LineIndex, LayoutBox? BBox);
    private sealed record FrontMatterResult(bool IsFrontMatter, List<string> Reasons);

    private sealed class TextLine
    {
        public TextLine(List<Word> words)
        {
            Words = words;
        }

        public List<Word> Words { get; }
        public string Text { get; private set; } = "";
        public double Left { get; private set; }
        public double Right { get; private set; }
        public double Top { get; private set; }
        public double Bottom { get; private set; }

        public TextLine Normalize()
        {
            var ordered = Words.OrderBy(w => w.BoundingBox.Left).ToList();
            Text = Collapse(string.Join(" ", ordered.Select(w => w.Text)));
            Left = ordered.Min(w => w.BoundingBox.Left);
            Right = ordered.Max(w => w.BoundingBox.Right);
            Top = ordered.Max(w => w.BoundingBox.Top);
            Bottom = ordered.Min(w => w.BoundingBox.Bottom);
            return this;
        }
    }
}
