using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;

public enum DocType
{
    AcademicResearch,
    BusinessCase,
    LegalCase,
    UnsupportedOther
}

public record DocTypeResult(
    DocType DocType,
    float Confidence,
    List<string> Signals,
    string Reason,
    List<(string label, float score)> Top2
);

public static class DocTypeClassifier
{
    const int MAX_PAGES = 8;
    const float MIN_SCORE_TO_ACCEPT = 2.0f;
    const float MIN_CONFIDENCE = 0.50f;

    public static DocTypeResult Evaluate(IEnumerable<dynamic> chunks)
    {
        var first = chunks
            .Where(x => (int)x.Page >= 1 && (int)x.Page <= MAX_PAGES)
            .OrderBy(x => (int)x.Page)
            .Take(250)
            .Select(x => $"{x.Preview}\n")
            .ToList();

        var sample = string.Join("\n", first);
        var sampleLower = sample.ToLowerInvariant();
        var compact = Regex.Replace(sampleLower, @"[^a-z0-9]+", "");

        if (string.IsNullOrWhiteSpace(sample) || sample.Length < 400)
        {
            return Unsupported("Very short/empty front matter.");
        }

        var academic = new (string pat, int w)[]
        {
            // Academic structure
            (@"\babstract\b", 3),
            (@"\bworking\s+paper\b", 4),
            (@"\bnber\b", 4),
            (@"\bkeywords?\b", 2),
            (@"\b(introduction|background)\b", 2),
            (@"\bmethods?\b|\bmethodology\b|\bmaterials?\s+and\s+methods?\b", 3),
            (@"\b(data\s+and\s+methods?|empirical\s+strategy)\b", 3),
            (@"\b(results?|findings?)\b", 3),
            (@"\bdiscussion\b", 3),
            (@"\bconclusions?\b|\blimitations?\b|\bfuture\s+work\b", 2),
            (@"\breferences\b|\bbibliography\b|\bworks\s+cited\b", 3),

            // Research / analysis language
            (@"\bstudy\b|\bpaper\b|\bresearch\b", 2),
            (@"\bmodel(s)?\b|\btheory\b|\bframework\b", 2),
            (@"\bempirical\b|\banalysis\b|\bevidence\b", 2),
            (@"\bexperiment(s)?\b|\bevaluation(s)?\b", 2),
            (@"\bhypothesis\b|\bvariable(s)?\b|\bsample\b", 2),

            // Economics / policy academic signals
            (@"\beconomic(s)?\b|\beconometric(s)?\b", 3),
            (@"\bmonetary\s+policy\b|\btrade\b|\bproductivity\b|\bindustry\b", 3),
            (@"\bpolicy\b|\bmarket(s)?\b|\baggregate\b", 1),

            // CS / technical research signals
            (@"\barchitecture\b|\balgorithm\b", 2),
            (@"\bneural\s+network(s)?\b|\btransformer(s)?\b|\bconvolutional\b|\bresidual\b", 2),
            (@"\bdataset(s)?\b|\bdata\s+set(s)?\b|\btraining\b|\bvalidation\b|\btest\s+set\b", 1),
            (@"\baccuracy\b|\bprecision\b|\brecall\b|\bf1[-\s]?score\b|\brmse\b|\bmse\b|\bauroc\b|\bbleu\b", 1),

            // Publication / citation signals
            (@"doi:\s*\S+", 2),
            (@"arxiv:\s*\S+", 2),
            (@"\([A-Z][A-Za-z\-]+,\s*20\d{2}\)", 2),
            (@"\[\d+\]", 1),
            (@"\breceived\b.*\baccepted\b", 1),
            (@"\baffiliations?\b|\bcorresponding\s+author\b", 1),
        };

        var business = new (string pat, int w)[]
        {
            // Strong business case identity
            (@"\bexhibit\s+\d+\b", 4),
            (@"\b(teaching\s+note|learning\s+objectives?)\b", 4),
            (@"\bcase\s+questions?\b|\bdiscussion\s+questions?\b", 4),
            (@"\byou are\b.*(manager|ceo|analyst|consultant)", 4),

            // Business decision framing
            (@"\b(alternatives?|options?)\b", 2),
            (@"\brecommendation(s)?\b", 3),
            (@"\bcompany\s+overview\b", 3),
            (@"\bdecision\s+maker\b|\bmanagerial\b", 3),

            // Business context
            (@"\bas of\s+\w+\s+\d{4}\b", 2),
            (@"\brevenue\b|\bcost(s)?\b|\bprofit(s)?\b|\bmarket\s+share\b", 1),
            (@"\bstrategy\b|\bcompetitive\b|\bcustomer(s)?\b", 1)
        };

        var legal = new (string pat, int w)[]
        {
            // Strong legal identity
            (@"\bv\.\b", 5),
            (@"\b(plaintiff|defendant|appellant|appellee|respondent|petitioner)\b", 5),
            (@"\bcourt\b|\bjudge\b|\bjustice\b|\bjury\b", 4),
            (@"\bstatute\b|\bprecedent\b|\bcase\s+law\b", 4),
            (@"\bheld\b|\bholding\b|\bdisposition\b", 3),
            (@"\bopinion\b|\bappeal\b|\bruling\b|\bjudgment\b", 3),

            // Reporter cites
            (@"\b\d{1,3}\s+(u\.s\.|f\.3d|f\.2d|s\.ct\.|scc|n\.y\.s\.\d)\b", 3)
        };

        var unsupported = new (string pat, int w)[]
        {
            (@"\bcurriculum\s+vitae\b|\bcv\b", 4),
            (@"\bresume\b", 4),
            (@"\bexperience\b", 2),
            (@"\beducation\b", 2),
            (@"\bskills?\b", 2),
            (@"\bprojects?\b", 2),
            (@"\bcertifications?\b", 2),
            (@"\blanguages?\b", 1),
            (@"\bagenda\b", 2),
            (@"\b(invoice|brochure|flyer)\b", 2)
        };

        int scoreAcademic = Score(sampleLower, academic);
        int scoreBusiness = Score(sampleLower, business);
        int scoreLegal = Score(sampleLower, legal);
        int scoreBlock = Score(sampleLower, unsupported);

        // Handle smashed PDF text like "AbstractThe..." or "1.Introduction..."
        if (HasLoose(compact, "abstract")) scoreAcademic += 3;
        if (HasLoose(compact, "introduction")) scoreAcademic += 2;
        if (HasLoose(compact, "references")) scoreAcademic += 3;
        if (HasLoose(compact, "workingpaper")) scoreAcademic += 4;
        if (HasLoose(compact, "nberworkingpaper")) scoreAcademic += 4;
        if (HasLoose(compact, "neuralnetwork") || HasLoose(compact, "residuallearning")) scoreAcademic += 2;

        bool strongLegalIdentity =
            Regex.IsMatch(sampleLower, @"\bv\.\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(sampleLower, @"\b(plaintiff|defendant|appellant|appellee|respondent|petitioner)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(sampleLower, @"\b(court|judge|justice|statute|precedent|case\s+law|ruling|judgment)\b", RegexOptions.IgnoreCase);

        bool strongBusinessIdentity =
            Regex.IsMatch(sampleLower, @"\bexhibit\s+\d+\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(sampleLower, @"\b(teaching\s+note|case\s+questions?|discussion\s+questions?)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(sampleLower, @"\byou are\b.*(manager|ceo|analyst|consultant)", RegexOptions.IgnoreCase);

        // Legal must have legal identity. Words like "rules", "policy", "analysis" are not enough.
        if (!strongLegalIdentity)
        {
            scoreLegal = 0;
        }

        // Business case needs case-study identity. Otherwise management papers should remain academic.
        if (!strongBusinessIdentity && scoreBusiness < 5)
        {
            scoreBusiness = Math.Min(scoreBusiness, 1);
        }

        // Strong unsupported documents should still be blocked.
        if (scoreBlock >= 6 && scoreBlock >= Math.Max(scoreAcademic, Math.Max(scoreBusiness, scoreLegal)))
        {
            return Unsupported($"Unsupported patterns dominated (score={scoreBlock}).");
        }

        var raw = new List<(string label, int score)>
        {
            ("academic_research", scoreAcademic),
            ("business_case", scoreBusiness),
            ("legal_case", scoreLegal)
        }.OrderByDescending(x => x.score).ToList();

        var top = raw[0];
        var second = raw[1];

        float conf = SoftmaxTop(new float[] { scoreAcademic, scoreBusiness, scoreLegal });

        if (top.score < MIN_SCORE_TO_ACCEPT || conf < MIN_CONFIDENCE)
        {
            return Unsupported($"Low separation or weak signals (top={top.label} score={top.score}, conf={conf:0.00}).");
        }

        var docType = top.label switch
        {
            "academic_research" => DocType.AcademicResearch,
            "business_case" => DocType.BusinessCase,
            "legal_case" => DocType.LegalCase,
            _ => DocType.UnsupportedOther
        };

        var signals = new List<string>();
        if (scoreAcademic > 0) signals.Add($"Academic:{scoreAcademic}");
        if (scoreBusiness > 0) signals.Add($"Business:{scoreBusiness}");
        if (scoreLegal > 0) signals.Add($"Legal:{scoreLegal}");
        if (scoreBlock > 0) signals.Add($"Unsupported:{scoreBlock}");
        if (strongLegalIdentity) signals.Add("StrongLegalIdentity");
        if (strongBusinessIdentity) signals.Add("StrongBusinessIdentity");

        string reason =
            $"Top={top.label} (score {top.score}) over {second.label} (score {second.score}); conf={conf:0.00}.";

        return new DocTypeResult(
            docType,
            conf,
            signals,
            reason,
            raw.Take(2).Select(x => (x.label, (float)x.score)).ToList()
        );
    }

    static int Score(string text, (string pat, int w)[] rules)
    {
        int s = 0;
        foreach (var (pat, w) in rules)
        {
            if (Regex.IsMatch(text, pat, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                s += w;
        }
        return s;
    }

    static bool HasLoose(string compact, string term)
    {
        var clean = Regex.Replace(term.ToLowerInvariant(), @"[^a-z0-9]+", "");
        return compact.Contains(clean);
    }

    static float SoftmaxTop(float[] xs)
    {
        float max = xs.Max();
        var exps = xs.Select(v => MathF.Exp(v - max)).ToArray();
        float sum = exps.Sum();
        return sum == 0 ? 0f : exps.Max() / sum;
    }

    static DocTypeResult Unsupported(string why) =>
        new DocTypeResult(DocType.UnsupportedOther, 0.0f, new List<string>(), why, new List<(string, float)>());
}

public static class DocTypePersistence
{
    public static void Save(Guid uploadId, IWebHostEnvironment env, DocTypeResult result)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", $"docclass-{uploadId}.json");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    public static bool TryLoad(Guid uploadId, IWebHostEnvironment env, out DocTypeResult? result)
    {
        var path = Path.Combine(env.ContentRootPath, "uploads", $"docclass-{uploadId}.json");
        if (!File.Exists(path))
        {
            result = null;
            return false;
        }

        var json = File.ReadAllText(path);
        result = JsonSerializer.Deserialize<DocTypeResult>(json);
        return result != null;
    }
}