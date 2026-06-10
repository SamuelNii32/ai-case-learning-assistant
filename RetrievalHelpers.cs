using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public record IndexedChunk(int Page, ReadOnlyMemory<float> Vec, string Preview);


// Return shape used by both routes
public record TopChunk(int Page, string Preview, float Score);


public static class QaRetrieval
{
    // Improved query understanding with more patterns
    public static bool IsListy(string q)
    {
        var s = q ?? string.Empty;

        // Common list verbs & phrasings
        if (Regex.IsMatch(s, @"\b(list|all|which|enumerate|show|show me|give|give me|name|return|extract|identify|find all|find every|every|provide|report|catalog|compile|what\s+are|what\s+were|how many|count)\b", RegexOptions.IgnoreCase))
            return true;

        // Numeric cues
        if (Regex.IsMatch(s, @"[%+]", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(s, @"\b(20\d{2}|19\d{2})\b", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(s, @"\b(date|dates|range|ranges|deadline|deadlines)\b", RegexOptions.IgnoreCase)) return true;

        return false;
    }

    // Safe cosine similarity
    public static float SafeCosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0 || b.Length == 0)
            return 0f;
        return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(a, b);
    }

    // Tokenize to alnum lowercase
    private static string[] Tokens(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        return Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]{2,}")
                    .Select(m => m.Value)
                    .ToArray();
    }

    // Small, hand-tuned synonym expansion for academic-style questions
    private static HashSet<string> ExpandQueryTerms(HashSet<string> original)
    {
        // Start with the original tokens
        var expanded = new HashSet<string>(original);

        void AddGroup(string[] keys, string[] synonyms)
        {
            if (!keys.Any(k => original.Contains(k))) return;
            foreach (var s in synonyms)
                expanded.Add(s);
        }

        // limitations / weaknesses / drawbacks
        AddGroup(
            new[] { "limitations", "limitation" },
            new[] { "weakness", "weaknesses", "drawback", "drawbacks", "constraint", "constraints", "challenge", "challenges" }
        );

        // findings / results / effects
        AddGroup(
            new[] { "findings", "finding", "results", "result" },
            new[] { "outcome", "outcomes", "impact", "impacts", "effect", "effects" }
        );

        // methodology / methods / approach
        AddGroup(
            new[] { "methodology", "methods", "method" },
            new[] { "approach", "design", "experimental" }
        );

        // future work / improvements
        AddGroup(
            new[] { "future", "improvements", "improvement", "recommendations", "recommendation" },
            new[] { "extension", "extensions", "further", "ongoing" }
        );

        // external validity / generalization
        AddGroup(
            new[] { "external", "validity", "generalization", "generalizability" },
            new[] { "replication", "replications", "scaling", "scaleup", "scalability" }
        );

        return expanded;
    }


    // IMPROVED: More generous lexical scoring
    private static float LexicalScore(string preview, HashSet<string> qset)
    {
        if (string.IsNullOrEmpty(preview) || qset.Count == 0) return 0f;
        var p = preview.ToLowerInvariant();

        float s = 0f;
        int matchCount = 0;

        foreach (var t in qset)
        {
            if (p.Contains(t))
            {
                matchCount++;
                // Weight matches by term frequency
                int occurrences = Regex.Matches(p, Regex.Escape(t), RegexOptions.IgnoreCase).Count;
                s += 1f + (occurrences - 1) * 0.3f; // bonus for multiple occurrences
            }
        }

        // Bonus for high match ratio
        float matchRatio = (float)matchCount / qset.Count;
        if (matchRatio > 0.5f) s += 2f;
        if (matchRatio > 0.75f) s += 2f;

        // Context bonuses
        if (p.Contains("@")) s += 0.5f;
        if (Regex.IsMatch(p, @"\b\d{4}\b")) s += 0.25f;

        return s;
    }

    // IMPROVED: More generous boost
    private static float Boost(string preview, HashSet<string> qset)
    {
        var p = preview?.ToLowerInvariant() ?? "";
        float b = 0f;
        int matches = 0;

        foreach (var t in qset)
        {
            if (p.Contains(t))
            {
                matches++;
                b += 0.05f; // increased from 0.03f
            }
        }

        if (p.Contains("@")) b += 0.03f;

        // Extra boost for multiple term matches
        if (matches >= 3) b += 0.05f;

        return Math.Min(b, 0.20f); // increased cap from 0.10f
    }

    // IMPROVED: More generous keyword fallback
    public static List<TopChunk> KeywordFallback(List<IndexedChunk> list, string q, int k = 8)
    {
        var qset = ExpandQueryTerms(new HashSet<string>(Tokens(q)));
        if (qset.Count == 0) return new List<TopChunk>();

        // LOWERED threshold - accept any match
        return list
            .Select(x => new { x.Page, x.Preview, lex = LexicalScore(x.Preview, qset) })
            .Where(r => r.lex > 0) // accept ANY match
            .OrderByDescending(r => r.lex)
            .Take(k * 2) // get more candidates
            .Select(r => new TopChunk(r.Page, r.Preview, Math.Min(0.25f, r.lex / 10f))) // score based on lexical
            .ToList();
    }

    // IMPROVED: Main selection with better defaults
    public static List<TopChunk> SelectTop(
        List<IndexedChunk> list,
        ReadOnlySpan<float> qVec,
        string q,
        bool forStreaming)
    {
        bool listy = IsListy(q);
        var qset = ExpandQueryTerms(new HashSet<string>(Tokens(q)));

        // IMPROVED: More generous K values
        int K = listy ? 25 : (forStreaming ? 15 : 12); // increased from 20/10/10

        // ADJUSTED: Less aggressive weighting to favor embeddings
        const float alpha = 0.70f; // embedding weight (reduced from 0.85)
        const float beta = 0.30f;  // lexical weight (increased from 0.15)

        float[] qVecArr = qVec.ToArray();

        // 1) Score ALL candidates first
        var cands = list.Select(x =>
        {
            var cos = SafeCosine(qVecArr, x.Vec.Span);
            var lex = LexicalScore(x.Preview, qset);
            var boo = Boost(x.Preview, qset);
            var fin = alpha * cos + beta * lex + boo;
            return new Cand(x.Page, x.Preview, x.Vec, cos, lex, boo, fin);
        })
        .OrderByDescending(c => c.Final)
        .Take(Math.Max(K * 6, 20)) // increased oversample from K*4
        .ToList();

        // 2) ADJUSTED MMR - less diversity for better recall
        var picked = MMR(cands, K, lambda: 0.85f); // increased from 0.7f to favor relevance

        return picked.Select(c => new TopChunk(c.Page, c.Preview, c.Final)).ToList();
    }

    // Internal record
    private record Cand(int Page, string Preview, ReadOnlyMemory<float> Vec, float Cos, float Lex, float Boost, float Final);

    // IMPROVED MMR with better diversity balance
    private static List<Cand> MMR(List<Cand> cands, int K, float lambda)
    {
        var chosen = new List<Cand>();
        var remaining = new List<Cand>(cands);

        while (chosen.Count < K && remaining.Count > 0)
        {
            Cand? best = null;
            float bestScore = float.NegativeInfinity;

            foreach (var c in remaining)
            {
                float maxSim = 0f;
                foreach (var s in chosen)
                {
                    var sim = SafeCosine(c.Vec.Span, s.Vec.Span);
                    if (sim > maxSim) maxSim = sim;
                }

                // MMR score: balance relevance vs diversity
                float score = lambda * c.Final - (1 - lambda) * maxSim;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            if (best != null)
            {
                chosen.Add(best);
                remaining.Remove(best);
            }
            else
            {
                break; // safety
            }
        }

        return chosen;
    }
}

public static class ContextStitching
{
    public static List<TopChunk> ExpandWithNeighbors(
        List<IndexedChunk> all,
        List<TopChunk> picks,
        int sideNeighbors = 2,        // increased from 1
        int maxTotalNeighbors = 10)   // increased from 6
    {
        if (picks == null || picks.Count == 0) return picks ?? new List<TopChunk>();

        var order = new Dictionary<(int page, string preview), int>();
        for (int i = 0; i < all.Count; i++)
        {
            var key = (all[i].Page, all[i].Preview);
            if (!order.ContainsKey(key)) order[key] = i;
        }

        var result = new List<TopChunk>(picks);
        var seen = new HashSet<string>(picks.Select(p => $"{p.Page}\u0001{p.Preview}"));
        int added = 0;

        foreach (var p in picks)
        {
            var key = (p.Page, p.Preview);
            if (!order.TryGetValue(key, out var idx)) continue;

            for (int offset = 1; offset <= sideNeighbors; offset++)
            {
                if (added >= maxTotalNeighbors) break;

                // Previous neighbor on same page
                if (idx - offset >= 0 && all[idx - offset].Page == p.Page)
                {
                    var prev = all[idx - offset];
                    var k = $"{prev.Page}\u0001{prev.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(prev.Page, prev.Preview, p.Score * 0.95f));
                        added++;
                    }
                }

                // Next neighbor on same page
                if (added < maxTotalNeighbors && idx + offset < all.Count && all[idx + offset].Page == p.Page)
                {
                    var next = all[idx + offset];
                    var k = $"{next.Page}\u0001{next.Preview}";
                    if (seen.Add(k))
                    {
                        result.Add(new TopChunk(next.Page, next.Preview, p.Score * 0.95f));
                        added++;
                    }
                }
            }

            if (added >= maxTotalNeighbors) break;
        }

        result = result
            .OrderBy(t => order.TryGetValue((t.Page, t.Preview), out var i) ? i : int.MaxValue)
            .ToList();

        return result;
    }
}

