using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public record CategoryHint(string Name, string PromptHint);

public static class CategoryDetector
{
    public static CategoryHint Detect(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return new CategoryHint("none", "");

        var s = q.ToLowerInvariant();

        // Technologies / tech stack / skills ? group by category
        if (Regex.IsMatch(s, @"\b(tech(?:nologies?)?|tech\s*stack|technology\s*stack|stack|skills|technical\s+skills)\b"))
            return new CategoryHint(
                "tech_group",
                "Group the answer into labeled sections: " +
                "1) Programming languages, 2) Frameworks & libraries, 3) Databases & data stores, " +
                "4) Tools & platforms (including DevOps/hosting), 5) Methodologies & practices. " +
                "Under each section, include only items that strictly belong to that category and exclude close neighbors. " +
                "If a section has no items, omit it."
            );


        // Programming languages
        if (Regex.IsMatch(s, @"\b(programming\s+languages?|coding\s+languages?)\b"))
            return new CategoryHint("programming_languages",
                "For programming languages, exclude frameworks, libraries, tools, databases, and model names.");

        // Frameworks / libraries
        if (Regex.IsMatch(s, @"\b(frameworks?|libraries|packages|toolkits?)\b"))
            return new CategoryHint("frameworks_libraries",
                "For frameworks and libraries, exclude programming languages, databases, and general tools.");

        // Databases / data stores
        if (Regex.IsMatch(s, @"\b(databases?|data\s*stores?|dbs?)\b"))
            return new CategoryHint("databases",
                "For databases and data stores, exclude programming languages, frameworks/libraries, and tools.");

        // Schools
        if (Regex.IsMatch(s, @"\b(universit(?:y|ies)|college(?:s)?|school(?:s)?)\b"))
            return new CategoryHint("schools",
                "For universities/colleges/schools, exclude degrees, departments, programs, and locations.");

        // People
        if (Regex.IsMatch(s, @"\b(people|persons|person\s+names?|authors?|speakers?|presenters?)\b"))
            return new CategoryHint("people",
                "For people, include person names only; exclude organizations, teams, and roles without names.");

        // Organizations
        if (Regex.IsMatch(s, @"\b(organizations?|companies|institutions|agencies)\b"))
            return new CategoryHint("organizations",
                "For organizations, exclude person names and job titles.");

        // Countries
        if (Regex.IsMatch(s, @"\b(countries?)\b"))
            return new CategoryHint("countries",
                "For countries, exclude cities, states/provinces, and regions.");

        // Dates / date ranges (months, years, deadlines)
        if (Regex.IsMatch(s, @"\b(dates?|date\s*ranges?|deadlines?)\b") ||
            Regex.IsMatch(s, @"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b") ||
            Regex.IsMatch(s, @"\b(19|20)\d{2}\b"))
            return new CategoryHint("dates",
                "For dates, return only explicit date expressions as written (e.g., 11/2021–07/2023); exclude durations without dates.");

        // Quantified metrics / achievements
        if (Regex.IsMatch(s, @"%|percent|percentage|\bplus\b|\b\+\b|\bmetrics?\b|\bachievements?\b"))
            return new CategoryHint("metrics",
                "For quantified achievements or metrics, return only items that include a percentage (%) or a plus-count (+), exactly as written.");

        return new CategoryHint("none", "");
    }
}

// ---------- Query normalization ----------
public static class QueryNormalization
{
    public static string Normalize(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return q ?? "";
        var s = q.Trim();

        // Strip polite prefixes
        s = Regex.Replace(s, @"^\s*(can you|could you|please|kindly|would you|i want to|i would like to|could u|can u)\s+", "", RegexOptions.IgnoreCase);

        // Map synonyms to tighter phrasing
        s = Regex.Replace(s, @"\b(name of (the )?document)\b", "document title", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(pdf title|title of the pdf)\b", "document title", RegexOptions.IgnoreCase);
        // QueryNormalization.Normalize(...)
        s = Regex.Replace(s, @"\b(title\s+of\s+(this|the)\s+(document|pdf))\b",
                          "document title", RegexOptions.IgnoreCase);


        // Collapse whitespace
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }
}


public enum SectionIntent
{
    None, Abstract, Title, Authors, Affiliations, Introduction, Conclusion, References, Keywords
}

public static class SectionSwitchboard
{
    public static SectionIntent Detect(string q)
    {
        var s = q?.ToLowerInvariant() ?? "";
        if (Regex.IsMatch(s,
        @"\b(what\s+is\s+the\s+(document|paper|thesis)\s+title\b|" +
        @"give\s+me\s+the\s+(document|paper|thesis)\s+title\b|" +
        @"title\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.Title;
        }
        if (Regex.IsMatch(s,
                @"\b(what\s+is\s+the\s+abstract\b|" +
                @"give\s+me\s+the\s+abstract\b|" +
                @"abstract\s+of\s+this\s+(paper|document|thesis)\b|" +
                @"show\s+the\s+abstract\b)"))
        {
            return SectionIntent.Abstract;
        }
        // Only treat as an "authors" question if it's explicitly about listing / naming them
        if (Regex.IsMatch(s,
                @"\b(who\s+(are|is)\s+the\s+authors?\b|" +
                @"list\s+the\s+authors?\b|" +
                @"author\s+names?\b|" +
                @"authors?\s+of\s+this\s+(paper|document))",
                RegexOptions.IgnoreCase))
        {
            return SectionIntent.Authors;
        }

        if (Regex.IsMatch(s,
                @"\b(what\s+are\s+the\s+affiliations?\b|" +
                @"list\s+the\s+affiliations?\b|" +
                @"affiliations?\s+of\s+the\s+authors?\b|" +
                @"which\s+institutions?\s+are\s+the\s+authors?\s+from\b)"))
        {
            return SectionIntent.Affiliations;
        }
        if (Regex.IsMatch(s, @"\b(introduction|background)\b")) return SectionIntent.Introduction;
        if (Regex.IsMatch(s, @"\b(conclusion|conclusions)\b")) return SectionIntent.Conclusion;
        if (Regex.IsMatch(s,
               @"\b((list|show|give)\s+the\s+(references|bibliography|works\s+cited)\b|" +
               @"what\s+are\s+the\s+(references|bibliography|works\s+cited)\b|" +
               @"(references|bibliography|works\s+cited)\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.References;
        }
        if (Regex.IsMatch(s,
               @"\b(what\s+are\s+the\s+keywords?\b|" +
               @"list\s+the\s+keywords?\b|" +
               @"keywords?\s+of\s+this\s+(paper|document|thesis)\b)"))
        {
            return SectionIntent.Keywords;
        }

        return SectionIntent.None;
    }

    public static List<TopChunk> FindSection(List<IndexedChunk> list, SectionIntent intent)
    {
        string pattern = intent switch
        {
            SectionIntent.Abstract => @"\babstract\b",
            SectionIntent.Introduction => @"\bintroduction\b",
            SectionIntent.Conclusion => @"\bconclusions?\b",
            SectionIntent.References => @"\b(references|bibliography|works\s+cited)\b",
            SectionIntent.Keywords => @"\bkeywords?\b",
            // Authors/Affiliations are weak as headings; still try
            SectionIntent.Authors => @"\bauthors?\b",
            SectionIntent.Affiliations => @"\baffiliations?\b",
            _ => ""
        };
        if (string.IsNullOrEmpty(pattern)) return new List<TopChunk>();

        var hits = list.Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
                       .GroupBy(x => x.Page)
                       .Select(g => g.First())
                       .OrderBy(x => x.Page)
                       .Select(x => new TopChunk(x.Page, x.Preview, 0.5f))
                       .ToList();
        return hits;


    }

    // Heuristic finders for methods / results-like sections
    public static List<TopChunk> FindMethodLikeSections(List<IndexedChunk> list)
    {
        // Look for headings like "Methods", "Materials and Methods", "Methodology"
        var pattern = @"\b(methods?|materials and methods|methodology)\b";

        var hits = list
            .Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
            .GroupBy(x => x.Page)
            .Select(g => g.First())
            .OrderBy(x => x.Page)
            .Select(x => new TopChunk(x.Page, x.Preview, 0.6f))
            .ToList();

        return hits;
    }

    public static List<TopChunk> FindFindingsLikeSections(List<IndexedChunk> list)
    {
        // Look for sections like "Results", "Findings", "Discussion"
        var pattern = @"\b(results?|findings?|results and discussion|discussion)\b";

        var hits = list
            .Where(x => Regex.IsMatch(x.Preview ?? "", pattern, RegexOptions.IgnoreCase))
            .GroupBy(x => x.Page)
            .Select(g => g.First())
            .OrderBy(x => x.Page)
            .Select(x => new TopChunk(x.Page, x.Preview, 0.6f))
            .ToList();

        return hits;
    }
}

public static class QueryWordHelpers
{
    public static int WordToInt(string w) => (w ?? "").ToLowerInvariant() switch
    {
        "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        "six" => 6,
        "seven" => 7,
        "eight" => 8,
        "nine" => 9,
        "ten" => 10,
        _ => 5
    };
}