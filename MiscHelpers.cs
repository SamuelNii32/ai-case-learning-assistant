using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;

public static class MiscHelpers
{
    public static List<(int page, string text)> ExtractAnchorsFromPages(
        string pdfPath,
        IEnumerable<int> pages,
        int maxTotal = 3,
        int maxChars = 220)
    {
        var anchors = new List<(int page, string text)>();
        try
        {
            if (!File.Exists(pdfPath)) return anchors;

            using var doc = PdfPigDoc.Open(pdfPath);

            foreach (var p in pages.Distinct().OrderBy(x => x))
            {
                if (p < 1 || p > doc.NumberOfPages) continue;

                var page = doc.GetPage(p);
                var raw = page.Text ?? string.Empty;
                var snippet = TakeFirstSentences(raw, maxChars);
                if (string.IsNullOrWhiteSpace(snippet)) continue;

                anchors.Add((p, snippet));
                if (anchors.Count >= maxTotal) break;
            }
        }
        catch
        {
            // swallow — return whatever we could extract
        }

        return anchors;
    }

    public static string TakeFirstSentences(string text, int maxChars)
    {
        var clean = CollapseWhitespace(text);
        if (clean.Length <= maxChars) return clean;

        var softLimit = Math.Max(100, Math.Min(maxChars, clean.Length));
        var dot = clean.IndexOf('.', softLimit);
        var cut = dot > 0 && dot < softLimit + 120 ? dot + 1 : maxChars;
        if (cut > clean.Length) cut = clean.Length;

        return clean.Substring(0, cut).Trim() + "…";
    }

    public static string CollapseWhitespace(string s)
    {
        return Regex.Replace(s ?? "", @"\s+", " ").Trim();
    }
}

public static class PdfImageUtils
{
    private sealed class ImageCounterListener : IEventListener
    {
        public int Count { get; private set; }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_IMAGE)
                Count++;
        }

        public ICollection<EventType> GetSupportedEvents() => null!;
    }

    private sealed class ImageCounterByPageListener : IEventListener
    {
        private readonly Dictionary<int, int> _counts = new();
        private int _page;

        public void SetPage(int page) => _page = page;

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE)
                return;

            _counts.TryGetValue(_page, out var count);
            _counts[_page] = count + 1;
        }

        public ICollection<EventType> GetSupportedEvents() => null!;

        public Dictionary<int, int> Counts => _counts;
    }

    public static int CountRasterImagesExact(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        int total = 0;

        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
        {
            var listener = new ImageCounterListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(pdf.GetPage(i));
            total += listener.Count;
        }

        return total;
    }

    public static Dictionary<int, int> CountRasterImagesByPage(string path)
    {
        using var pdf = new PdfDocument(new PdfReader(path));
        var listener = new ImageCounterByPageListener();
        var processor = new PdfCanvasProcessor(listener);

        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
        {
            listener.SetPage(i);
            processor.ProcessPageContent(pdf.GetPage(i));
        }

        return listener.Counts;
    }
}
