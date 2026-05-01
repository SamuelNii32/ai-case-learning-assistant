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
    const int MIN_SAMPLE_CHARS = 400;
    const int MAX_CHUNKS_PER_PAGE = 18;

    public static DocTypeResult Evaluate(IEnumerable<dynamic> chunks)
    {
        var sampled = BuildSample(chunks);
        var sample = sampled.Text;
        var sampleLower = sample.ToLowerInvariant();
        var compact = Regex.Replace(sampleLower, @"[^a-z0-9]+", "");

        var evidence = new Evidence();

        evidence.AcademicStructure += Score(sampleLower, AcademicStructureRules, evidence.AcademicHits);
        evidence.AcademicLanguage += Score(sampleLower, AcademicLanguageRules, evidence.AcademicHits);
        evidence.AcademicCs += Score(sampleLower, AcademicCsRules, evidence.AcademicHits);
        evidence.AcademicCitation += Score(sampleLower, AcademicCitationRules, evidence.AcademicHits);
        evidence.AcademicStructure += ScoreLoose(compact, LooseAcademicStructure, evidence.AcademicHits);
        evidence.AcademicCs += ScoreLoose(compact, LooseAcademicCs, evidence.AcademicHits);

        evidence.BusinessIdentity += Score(sampleLower, BusinessIdentityRules, evidence.BusinessHits);
        evidence.BusinessLanguage += Score(sampleLower, BusinessLanguageRules, evidence.BusinessHits);
        evidence.LegalIdentity += Score(sampleLower, LegalIdentityRules, evidence.LegalHits);
        evidence.LegalLanguage += Score(sampleLower, LegalLanguageRules, evidence.LegalHits);
        evidence.Unsupported += Score(sampleLower, UnsupportedRules, evidence.UnsupportedHits);

        var scoreAcademic = evidence.AcademicScore;
        var scoreBusiness = evidence.BusinessScore;
        var scoreLegal = evidence.LegalScore;
        var scoreUnsupported = evidence.Unsupported;

        var strongBusinessIdentity = evidence.BusinessIdentity >= 4;
        var strongLegalIdentity = evidence.LegalIdentity >= 5;

        // A business case needs case-study identity. Otherwise strategy/market language in
        // academic papers should not pull the result away from AcademicResearch.
        if (!strongBusinessIdentity)
        {
            scoreBusiness = Math.Min(scoreBusiness, evidence.BusinessLanguage >= 5 ? 2 : 1);
        }

        // Legal classification requires legal identity, not just words like "rule" or "analysis".
        if (!strongLegalIdentity)
        {
            scoreLegal = 0;
        }

        var raw = new List<(string label, int score)>
        {
            ("academic_research", scoreAcademic),
            ("business_case", scoreBusiness),
            ("legal_case", scoreLegal)
        }.OrderByDescending(x => x.score).ToList();

        var top = raw[0];
        var second = raw[1];
        var conf = SoftmaxTop(new float[] { scoreAcademic, scoreBusiness, scoreLegal });
        var top2 = raw.Take(2).Select(x => (x.label, (float)x.score)).ToList();
        var signals = BuildSignals(evidence, sampled.Pages);

        if (scoreUnsupported >= 6 && scoreUnsupported >= Math.Max(scoreAcademic, Math.Max(scoreBusiness, scoreLegal)))
        {
            return Unsupported(
                $"Unsupported document signals dominated (unsupported={scoreUnsupported}, top={top.label} score={top.score}).",
                conf,
                signals,
                top2);
        }

        var acceptedAcademic =
            top.label == "academic_research" &&
            (
                scoreAcademic >= 5 ||
                evidence.AcademicStructure >= 3 ||
                evidence.AcademicCitation >= 3 ||
                (evidence.AcademicCs >= 3 && evidence.AcademicLanguage >= 1) ||
                (evidence.AcademicCs >= 4 && conf >= 0.55f)
            );

        var acceptedBusiness =
            top.label == "business_case" &&
            strongBusinessIdentity &&
            scoreBusiness >= 6;

        var acceptedLegal =
            top.label == "legal_case" &&
            strongLegalIdentity &&
            scoreLegal >= 7;

        if ((string.IsNullOrWhiteSpace(sample) || sample.Length < MIN_SAMPLE_CHARS) &&
            !acceptedAcademic &&
            !acceptedBusiness &&
            !acceptedLegal)
        {
            return Unsupported(
                $"Very short/empty classification sample with weak evidence (chars={sample.Length}).",
                conf,
                signals,
                top2);
        }

        if (!acceptedAcademic && !acceptedBusiness && !acceptedLegal)
        {
            return Unsupported(
                $"Weak or incomplete document-type evidence (top={top.label} score={top.score}, second={second.label} score={second.score}, conf={conf:0.00}).",
                conf,
                signals,
                top2);
        }

        var docType = top.label switch
        {
            "academic_research" => DocType.AcademicResearch,
            "business_case" => DocType.BusinessCase,
            "legal_case" => DocType.LegalCase,
            _ => DocType.UnsupportedOther
        };

        var reason =
            $"Top={top.label} (score {top.score}) over {second.label} (score {second.score}); " +
            $"conf={conf:0.00}; pages={string.Join(",", sampled.Pages)}.";

        return new DocTypeResult(docType, conf, signals, reason, top2);
    }

    private static Sample BuildSample(IEnumerable<dynamic> chunks)
    {
        var rows = chunks
            .Select(x => new
            {
                Page = (int)x.Page,
                Text = ((string?)x.Preview ?? "").Trim()
            })
            .Where(x => x.Page > 0 && !string.IsNullOrWhiteSpace(x.Text))
            .GroupBy(x => x.Page)
            .OrderBy(g => g.Key)
            .ToList();

        if (rows.Count == 0)
        {
            return new Sample("", new List<int>());
        }

        var pages = rows.Select(g => g.Key).OrderBy(x => x).ToList();
        var selectedPages = SelectRepresentativePages(pages);
        var selected = rows
            .Where(g => selectedPages.Contains(g.Key))
            .SelectMany(g => g.Take(MAX_CHUNKS_PER_PAGE).Select(x => $"-- Page {g.Key} --\n{x.Text}"))
            .ToList();

        return new Sample(string.Join("\n\n", selected), selectedPages.OrderBy(x => x).ToList());
    }

    private static HashSet<int> SelectRepresentativePages(List<int> pages)
    {
        var selected = new HashSet<int>();
        foreach (var p in pages.Take(4)) selected.Add(p);
        foreach (var p in pages.TakeLast(4)) selected.Add(p);

        if (pages.Count > 8)
        {
            var mid = pages.Count / 2;
            foreach (var p in pages.Skip(Math.Max(0, mid - 2)).Take(4)) selected.Add(p);
        }

        return selected;
    }

    private static int Score(string text, Rule[] rules, List<string> hits)
    {
        var score = 0;
        foreach (var rule in rules)
        {
            if (Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                score += rule.Weight;
                hits.Add(rule.Label);
            }
        }

        return score;
    }

    private static int ScoreLoose(string compact, Rule[] rules, List<string> hits)
    {
        var score = 0;
        foreach (var rule in rules)
        {
            if (compact.Contains(rule.Pattern))
            {
                score += rule.Weight;
                hits.Add(rule.Label);
            }
        }

        return score;
    }

    private static List<string> BuildSignals(Evidence e, List<int> pages)
    {
        var signals = new List<string>
        {
            $"SamplePages:{string.Join(",", pages)}",
            $"AcademicStructure:{e.AcademicStructure}",
            $"AcademicLanguage:{e.AcademicLanguage}",
            $"AcademicCs:{e.AcademicCs}",
            $"AcademicCitation:{e.AcademicCitation}",
            $"BusinessIdentity:{e.BusinessIdentity}",
            $"BusinessLanguage:{e.BusinessLanguage}",
            $"LegalIdentity:{e.LegalIdentity}",
            $"LegalLanguage:{e.LegalLanguage}",
            $"Unsupported:{e.Unsupported}"
        };

        signals.AddRange(e.AcademicHits.Distinct().Take(8).Select(x => $"AcademicHit:{x}"));
        signals.AddRange(e.BusinessHits.Distinct().Take(5).Select(x => $"BusinessHit:{x}"));
        signals.AddRange(e.LegalHits.Distinct().Take(5).Select(x => $"LegalHit:{x}"));
        signals.AddRange(e.UnsupportedHits.Distinct().Take(5).Select(x => $"UnsupportedHit:{x}"));
        return signals;
    }

    private static float SoftmaxTop(float[] xs)
    {
        var max = xs.Max();
        var exps = xs.Select(v => MathF.Exp(v - max)).ToArray();
        var sum = exps.Sum();
        return sum == 0 ? 0f : exps.Max() / sum;
    }

    private static DocTypeResult Unsupported(
        string why,
        float confidence,
        List<string> signals,
        List<(string label, float score)> top2) =>
        new(DocType.UnsupportedOther, confidence, signals, why, top2);

    private record Rule(string Label, string Pattern, int Weight);
    private record Sample(string Text, List<int> Pages);

    private sealed class Evidence
    {
        public int AcademicStructure { get; set; }
        public int AcademicLanguage { get; set; }
        public int AcademicCs { get; set; }
        public int AcademicCitation { get; set; }
        public int BusinessIdentity { get; set; }
        public int BusinessLanguage { get; set; }
        public int LegalIdentity { get; set; }
        public int LegalLanguage { get; set; }
        public int Unsupported { get; set; }
        public int AcademicScore => AcademicStructure + AcademicLanguage + AcademicCs + AcademicCitation;
        public int BusinessScore => BusinessIdentity + BusinessLanguage;
        public int LegalScore => LegalIdentity + LegalLanguage;
        public List<string> AcademicHits { get; } = new();
        public List<string> BusinessHits { get; } = new();
        public List<string> LegalHits { get; } = new();
        public List<string> UnsupportedHits { get; } = new();
    }

    private static readonly Rule[] AcademicStructureRules =
    {
        new("abstract", @"\babstract\b", 4),
        new("keywords", @"\bkeywords?\b", 2),
        new("introduction", @"\b(?:\d+\.?\s*)?introduction\b", 3),
        new("related_work", @"\brelated\s+work\b|\bprior\s+work\b", 2),
        new("methodology", @"\bmethods?\b|\bmethodology\b|\bmaterials?\s+and\s+methods?\b|\bapproach\b", 2),
        new("experiments_section", @"\bexperiments?\b|\bexperimental\s+(setup|results)\b", 3),
        new("results_section", @"\bresults?\b|\bfindings?\b|\bevaluation\b", 2),
        new("discussion", @"\bdiscussion\b", 2),
        new("conclusion", @"\bconclusions?\b|\blimitations?\b|\bfuture\s+work\b", 3),
        new("references", @"\breferences\b|\bbibliography\b|\bworks\s+cited\b", 4)
    };

    private static readonly Rule[] AcademicLanguageRules =
    {
        new("paper_study_research", @"\bstudy\b|\bpaper\b|\bresearch\b", 2),
        new("model_framework", @"\bmodels?\b|\bframework\b|\barchitecture\b", 2),
        new("analysis_evidence", @"\bempirical\b|\banalysis\b|\bevidence\b", 2),
        new("hypothesis_variable_sample", @"\bhypothes(?:is|es)\b|\bvariables?\b|\bsample\b", 2),
        new("dataset_training", @"\bdatasets?\b|\bdata\s+sets?\b|\btraining\b|\bvalidation\b|\btest\s+sets?\b", 2),
        new("metrics", @"\baccuracy\b|\bprecision\b|\brecall\b|\bf1[-\s]?score\b|\brmse\b|\bmse\b|\bauroc\b|\bperplexity\b", 1),
        new("working_paper", @"\bworking\s+paper\b|\bnber\b", 4)
    };

    private static readonly Rule[] AcademicCsRules =
    {
        new("transformer", @"\btransformers?\b", 4),
        new("attention", @"\bself[-\s]?attention\b|\bmulti[-\s]?head\s+attention\b|\battention\s+(mechanism|heads?|layers?)\b", 4),
        new("encoder_decoder", @"\bencoders?\b|\bdecoders?\b|\bencoder[-\s]?decoder\b", 3),
        new("translation", @"\bmachine\s+translation\b|\btranslation\s+quality\b|\bsequence\s+transduction\b|\bneural\s+machine\s+translation\b", 3),
        new("nlp_benchmarks", @"\bbleu\b|\bwmt\b|\benglish[-\s]?to[-\s]?german\b|\benglish[-\s]?to[-\s]?french\b", 2),
        new("neural_network", @"\bneural\s+networks?\b|\bconvolutional\b|\brecurrent\b|\blstm\b|\bgru\b", 2),
        new("ablations_baselines", @"\bablation\b|\bbaselines?\b|\bstate[-\s]?of[-\s]?the[-\s]?art\b", 2),
        new("optimization", @"\bgradient\b|\boptimizer\b|\badam\b|\blearning\s+rate\b|\bepochs?\b", 1)
    };

    private static readonly Rule[] AcademicCitationRules =
    {
        new("doi", @"doi:\s*\S+", 2),
        new("arxiv", @"arxiv:\s*\S+", 2),
        new("apa_citation", @"\([A-Z][A-Za-z\-]+,\s*(?:19|20)\d{2}\)", 2),
        new("numeric_citation", @"\[(?:\d{1,3}|[A-Za-z][^\]]+,\s*(?:19|20)\d{2})\]", 1),
        new("author_affiliation", @"\baffiliations?\b|\bcorresponding\s+author\b|\bdepartment\s+of\b|\buniversity\b", 1),
        new("received_accepted", @"\breceived\b.*\baccepted\b", 1)
    };

    private static readonly Rule[] LooseAcademicStructure =
    {
        new("loose_abstract", "abstract", 4),
        new("loose_introduction", "introduction", 3),
        new("loose_related_work", "relatedwork", 2),
        new("loose_experiments", "experiments", 3),
        new("loose_conclusion", "conclusion", 3),
        new("loose_references", "references", 4)
    };

    private static readonly Rule[] LooseAcademicCs =
    {
        new("loose_self_attention", "selfattention", 4),
        new("loose_multi_head_attention", "multiheadattention", 4),
        new("loose_encoder_decoder", "encoderdecoder", 3),
        new("loose_machine_translation", "machinetranslation", 3),
        new("loose_transformer", "transformer", 4)
    };

    private static readonly Rule[] BusinessIdentityRules =
    {
        new("exhibit", @"\bexhibit\s+\d+\b", 4),
        new("teaching_note", @"\bteaching\s+note\b|\blearning\s+objectives?\b", 4),
        new("case_questions", @"\bcase\s+questions?\b|\bdiscussion\s+questions?\b", 4),
        new("decision_role", @"\byou\s+are\b.*\b(manager|ceo|analyst|consultant|director)\b", 4),
        new("case_framing", @"\bcase\s+study\b|\bcase\s+analysis\b", 3)
    };

    private static readonly Rule[] BusinessLanguageRules =
    {
        new("alternatives", @"\balternatives?\b|\boptions?\b|\bscenarios?\b", 2),
        new("recommendation", @"\brecommendation(s)?\b|\brecommended\s+course\b", 3),
        new("company_overview", @"\bcompany\s+overview\b|\bcompany\s+background\b", 3),
        new("decision_maker", @"\bdecision\s+maker\b|\bmanagerial\b|\bstrategic\s+decision\b", 3),
        new("financials", @"\brevenue\b|\bcosts?\b|\bprofits?\b|\bmarket\s+share\b|\bnpv\b|\birr\b|\bcash\s+flow\b", 1),
        new("strategy", @"\bstrategy\b|\bcompetitive\b|\bcustomers?\b|\bmarket\b", 1)
    };

    private static readonly Rule[] LegalIdentityRules =
    {
        new("versus", @"\bv\.\b|\bversus\b", 5),
        new("parties", @"\bplaintiff\b|\bdefendant\b|\bappellant\b|\bappellee\b|\brespondent\b|\bpetitioner\b", 5),
        new("court", @"\bcourt\b|\bjudge\b|\bjustice\b|\bjury\b", 4),
        new("legal_authority", @"\bstatute\b|\bprecedent\b|\bcase\s+law\b|\bregulation\b", 4),
        new("reporter_cite", @"\b\d{1,3}\s+(u\.s\.|f\.3d|f\.2d|s\.ct\.|scc|n\.y\.s\.\d)\b", 4)
    };

    private static readonly Rule[] LegalLanguageRules =
    {
        new("holding", @"\bheld\b|\bholding\b|\bdisposition\b", 3),
        new("opinion", @"\bopinion\b|\bappeal\b|\bruling\b|\bjudgment\b", 3),
        new("facts_issues_rule", @"\bfacts?\b|\bissues?\b|\brules?\b|\breasoning\b", 1)
    };

    private static readonly Rule[] UnsupportedRules =
    {
        new("cv", @"\bcurriculum\s+vitae\b|\bcv\b", 4),
        new("resume", @"\bresume\b", 4),
        new("work_history", @"\bexperience\b|\bemployment\b|\bwork\s+history\b", 2),
        new("education", @"\beducation\b|\bdegree\b|\bgpa\b", 2),
        new("skills", @"\bskills?\b|\bcertifications?\b|\blanguages?\b", 2),
        new("invoice", @"\binvoice\b|\breceipt\b|\bbill\s+to\b|\bamount\s+due\b", 4),
        new("marketing", @"\bbrochure\b|\bflyer\b|\bagenda\b|\bevent\s+schedule\b", 3)
    };
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
