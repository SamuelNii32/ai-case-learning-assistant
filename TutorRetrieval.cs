using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class TutorRetrieval
{



    public static async Task<TutorResponse> BuildAcademicBackgroundOverview(
     TutorSession session,
     ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to introduce the background section yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c3-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Background overview unavailable"
            );
        }

        var query = "background introduction prior work previous research literature related work problem question motivation gap context";
        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
            cites = new List<int> { 1 };

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "background_overview",
            "overview of the paper's background, problem, and prior work",
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            "background",
            narrative,
            chunkPreviews
        );

        var choices = new List<TutorChoice>
    {
        new("c3-1", choiceSet.c1, TutorAction.ExploreChildTopic, "problem_framing"),
        new("c3-2", choiceSet.c2, TutorAction.ExploreChildTopic, "prior_work"),
        new("c3-3", choiceSet.c3, TutorAction.ExploreChildTopic, "research_gap"),
        new(
            "c3-4",
            FocusMenuLabel(),
            TutorAction.ChangeFocus,
            "focus_menu"
        )
    };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: "Entered background focus"
        );
    }


    public static async Task<TutorResponse> BuildAcademicBackgroundResponse(
    TutorSession session,
    string childTarget,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to guide this background step yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("background-back", ReturnToFocusLabel("background"), TutorAction.ChangeFocus, "background")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Background retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "problem_framing" => "problem question motivation central issue why study matters research problem",
            "prior_work" => "prior work literature previous research background related work earlier studies",
            "research_gap" => "gap limitation missing unresolved problem contribution addresses weakness",
            _ => "background introduction literature problem gap"
        };

        var top = QaRetrieval.KeywordFallback(chunks, query, 5);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var topic = childTarget switch
        {
            "problem_framing" => "the problem or question the paper is trying to address",
            "prior_work" => "the prior work and literature context the paper builds on",
            "research_gap" => "the gap or limitation in earlier work that motivates the paper",
            _ => "the paper's background and motivation"
        };

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            $"background_{childTarget}",
            topic,
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            $"background_{childTarget}",
            narrative,
            chunkPreviews
        );

        var pending = childTarget switch
        {
            "problem_framing" => BuildInitialDrillChoices(session, "background", childTarget, "problem_detail", "why_problem_matters"),
            "prior_work" => BuildInitialDrillChoices(session, "background", childTarget, "prior_work_detail", "prior_work_connection"),
            "research_gap" => BuildInitialDrillChoices(session, "background", childTarget, "gap_detail", "contribution_positioning"),
            _ => new List<TutorDrillNode>()
        };

        var choices = childTarget switch
        {
            "problem_framing" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "problem_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "why_problem_matters"),
            new("c3-1-c", ReturnToFocusLabel("background"), TutorAction.ChangeFocus, "background")
        },
            "prior_work" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "prior_work_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "prior_work_connection"),
            new("c3-2-c", ReturnToFocusLabel("background"), TutorAction.ChangeFocus, "background")
        },
            "research_gap" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "gap_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "contribution_positioning"),
            new("c3-3-c", ReturnToFocusLabel("background"), TutorAction.ChangeFocus, "background")
        },
            _ => new List<TutorChoice>
        {
            new("background-back", ReturnToFocusLabel("background"), TutorAction.ChangeFocus, "background")
        }
        };

        string summary = childTarget switch
        {
            "problem_framing" => "Background: problem framing",
            "prior_work" => "Background: prior work",
            "research_gap" => "Background: research gap",
            _ => "Background"
        };

        SaveInitialDrillChoices(session, pending);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: summary
        );
    }
    public static async Task<TutorResponse> BuildAcademicMethodologyOverview(
     TutorSession session,
     ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to introduce the methodology section yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c2-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Methodology overview unavailable"
            );
        }

        var query = "method methodology approach design experiment experiments evaluation architecture setup data model";
        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
            cites = new List<int> { 1 };

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "methodology_overview",
            "overview of the paper's methodology",
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            "methodology",
            narrative,
            chunkPreviews
        );

        var choices = new List<TutorChoice>
    {
        new("c2-1", choiceSet.c1, TutorAction.ExploreChildTopic, "data_sources"),
        new("c2-2", choiceSet.c2, TutorAction.ExploreChildTopic, "measures"),
        new("c2-3", choiceSet.c3, TutorAction.ExploreChildTopic, "analysis_methods"),
        new(
            "c2-4",
            FocusMenuLabel(),
            TutorAction.ChangeFocus,
            "focus_menu"
        )
    };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: "Entered methodology focus"
        );
    }
    public static async Task<TutorResponse> BuildAcademicMethodologyResponse(
    TutorSession session,
    string childTarget,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to guide this methodology step yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("method-back", ReturnToFocusLabel("methodology"), TutorAction.ChangeFocus, "methodology")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Methodology retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "data_sources" => "data sources source material evidence sample dataset archive fieldwork corpus materials",
            "measures" => "measures variables indicators constructs categories definitions operationalized measurement",
            "analysis_methods" => "analysis methods comparison model estimation framework interpretation approach",
            _ => "methodology methods data analysis"
        };

        var top = QaRetrieval.SelectTop(
            chunks,
            ReadOnlySpan<float>.Empty,
            query,
            forStreaming: false
        );

        if (top.Count == 0)
        {
            top = QaRetrieval.KeywordFallback(chunks, query, 5);
        }

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        string narrative = childTarget switch
        {
            "data_sources" => await BuildDataSourcesNarrative(chat, chosen, cites),
            "measures" => await BuildMeasuresNarrative(chat, chosen, cites),
            "analysis_methods" => await BuildAnalysisNarrative(chat, chosen, cites),
            _ => $"I can’t find that in the document. [p:{cites[0]}]"
        };

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            $"methodology_{childTarget}",
            narrative,
            chunkPreviews
        );

        var pending = childTarget switch
        {
            "data_sources" => BuildInitialDrillChoices(session, "methodology", childTarget, "dataset_breadth", "source_credibility"),
            "measures" => BuildInitialDrillChoices(session, "methodology", childTarget, "variable_definition", "measurement_choices"),
            "analysis_methods" => BuildInitialDrillChoices(session, "methodology", childTarget, "main_metrics", "comparison_strategy"),
            _ => new List<TutorDrillNode>()
        };

        var choices = childTarget switch
        {
            "data_sources" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "dataset_breadth"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "source_credibility"),
            new("c2-1-c", ReturnToFocusLabel("methodology"), TutorAction.ChangeFocus, "methodology")
        },
            "measures" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "variable_definition"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "measurement_choices"),
            new("c2-2-c", ReturnToFocusLabel("methodology"), TutorAction.ChangeFocus, "methodology")
        },
            "analysis_methods" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "main_metrics"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "comparison_strategy"),
            new("c2-3-c", ReturnToFocusLabel("methodology"), TutorAction.ChangeFocus, "methodology")
        },
            _ => new List<TutorChoice>
        {
            new("method-back", ReturnToFocusLabel("methodology"), TutorAction.ChangeFocus, "methodology")
        }
        };

        string summary = childTarget switch
        {
            "data_sources" => "Methodology: data sources",
            "measures" => "Methodology: measures",
            "analysis_methods" => "Methodology: analysis methods",
            _ => "Methodology"
        };

        SaveInitialDrillChoices(session, pending);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: summary
        );
    }

    private static async Task<string> BuildDataSourcesNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "methodology",
            "data sources",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }

    private static async Task<string> BuildMeasuresNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "methodology",
            "measures",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }

    private static async Task<string> BuildAnalysisNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "methodology",
            "analysis methods",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }


    public static async Task<TutorResponse> BuildAcademicFindingsOverview(
    TutorSession session,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to introduce the findings section yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c1-4", "Return to the focus menu.\nChoose another major part of the paper.", TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Findings overview unavailable"
            );
        }

        var query = "main findings results conclusions contributions performance evidence key outcomes";
        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
            cites = new List<int> { 1 };

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "findings_overview",
            "overview of the paper's main findings",
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            "findings",
            narrative,
            chunkPreviews
        );

        var choices = new List<TutorChoice>
    {
        new("c1-1", "Examine the supporting evidence\nWalk through the results that support the main claim.", TutorAction.ExploreChildTopic, "supporting_evidence"),
        new("c1-2", "Connect why it matters\nSee why the finding changes the paper's larger argument.", TutorAction.ExploreChildTopic, "why_it_matters"),
        new("c1-3", "Test limits or trade-offs\nLook at what the finding does not fully settle.", TutorAction.ExploreChildTopic, "limits_or_tradeoffs"),
        new(
            "c1-4",
            FocusMenuLabel(),
            TutorAction.ChangeFocus,
            "focus_menu"
        )
    };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: "Entered findings focus"
        );
    }
    public static async Task<TutorResponse> BuildAcademicFindingsResponse(
    TutorSession session,
    string childTarget,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to guide this findings step yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("findings-back", ReturnToFocusLabel("findings"), TutorAction.ChangeFocus, "findings")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Findings retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "supporting_evidence" => "main finding evidence results table benchmark reported outcome support claim",
            "why_it_matters" => "significance contribution importance implication why matters argument advances field",
            "limits_or_tradeoffs" => "limitation tradeoff caveat exception boundary condition cost constraint qualification",
            _ => "findings results conclusion discussion"
        };

        var top = QaRetrieval.KeywordFallback(chunks, query, 5);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        string narrative = childTarget switch
        {
            "supporting_evidence" => await BuildFindingsMeasurementNarrative(chat, chosen, cites),
            "why_it_matters" => await BuildFindingsRelationshipsNarrative(chat, chosen, cites),
            "limits_or_tradeoffs" => await BuildFindingsExceptionsNarrative(chat, chosen, cites),
            _ => $"I can’t find that in the document. [p:{cites[0]}]"
        };

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            $"findings_{childTarget}",
            narrative,
            chunkPreviews
        );

        var pending = childTarget switch
        {
            "supporting_evidence" => BuildInitialDrillChoices(session, "findings", childTarget, "read_the_result", "connect_evidence_to_claim"),
            "why_it_matters" => BuildInitialDrillChoices(session, "findings", childTarget, "argument_significance", "field_or_practice_impact"),
            "limits_or_tradeoffs" => BuildInitialDrillChoices(session, "findings", childTarget, "main_tradeoff", "boundary_condition"),
            _ => new List<TutorDrillNode>()
        };

        var choices = childTarget switch
        {
            "supporting_evidence" => new List<TutorChoice>
        {
            new("drill:0", "Read the main result\nSee exactly what evidence supports the claim.", TutorAction.DrillDeeper, "read_the_result"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "connect_evidence_to_claim"),
            new("c1-1-c", ReturnToFocusLabel("findings"), TutorAction.ChangeFocus, "findings")
        },
            "why_it_matters" => new List<TutorChoice>
        {
            new("drill:0", "Connect to the argument\nSee how this finding changes the paper's larger claim.", TutorAction.DrillDeeper, "argument_significance"),
            new("drill:1", "Look beyond the paper\nConsider why this finding matters for the field or practice.", TutorAction.DrillDeeper, "field_or_practice_impact"),
            new("c1-2-c", ReturnToFocusLabel("findings"), TutorAction.ChangeFocus, "findings")
        },
            "limits_or_tradeoffs" => new List<TutorChoice>
        {
            new("drill:0", "Test the main trade-off\nSee what the result gains and what it may give up.", TutorAction.DrillDeeper, "main_tradeoff"),
            new("drill:1", "Check the boundary\nLook at where the finding may not fully apply.", TutorAction.DrillDeeper, "boundary_condition"),
            new("c1-3-c", ReturnToFocusLabel("findings"), TutorAction.ChangeFocus, "findings")
        },
            _ => new List<TutorChoice>
        {
            new("findings-back", ReturnToFocusLabel("findings"), TutorAction.ChangeFocus, "findings")
        }
        };

        string summary = childTarget switch
        {
            "supporting_evidence" => "Findings: supporting evidence",
            "why_it_matters" => "Findings: why it matters",
            "limits_or_tradeoffs" => "Findings: limits and trade-offs",
            _ => "Findings"
        };

        SaveInitialDrillChoices(session, pending);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: summary
        );
    }

    private static async Task<string> BuildFindingsMeasurementNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "findings",
            "supporting evidence",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }

    private static async Task<string> BuildFindingsRelationshipsNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "findings",
            "why the finding matters",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }

    private static async Task<string> BuildFindingsExceptionsNarrative(
        ChatClient chat,
        List<TopChunk> chosen,
        List<int> cites)
    {
        if (chosen.Count == 0)
            return $"I can’t find that in the document. [p:{cites[0]}]";

        return await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "findings",
            "limits or trade-offs",
            chosen.Select(x => x.Preview).ToList(),
            cites
        );
    }

    public static async Task<TutorResponse> BuildAcademicConceptsOverview(
    TutorSession session,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to introduce the concepts section yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Concepts overview unavailable"
            );
        }

        var query = "concept definition term idea framework category construct measure indicator relationship theory model";
        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
            cites = new List<int> { 1 };

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "concepts_overview",
            "overview of the paper's key concepts, terms, measures, and conceptual relationships",
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            "concepts",
            narrative,
            chunkPreviews
        );

        var choices = new List<TutorChoice>
    {
        new("c4-1", choiceSet.c1, TutorAction.ExploreChildTopic, "core_concept"),
        new("c4-2", choiceSet.c2, TutorAction.ExploreChildTopic, "key_indicator"),
        new("c4-3", choiceSet.c3, TutorAction.ExploreChildTopic, "concept_connections"),
        new(
            "c4-4",
            FocusMenuLabel(),
            TutorAction.ChangeFocus,
            "focus_menu"
        )
    };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: "Entered concepts focus"
        );
    }

    public static async Task<TutorResponse> BuildAcademicConceptsResponse(
    TutorSession session,
    string childTarget,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to guide this concepts step yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Concepts retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "core_concept" => "defines definition meaning refers to term concept construct category distinction describes called understood as",
            "key_indicator" => "measured by operationalized indicator variable metric proxy coding scale index data measure how measured",
            "concept_connections" => "relationship between linked to associated with affects explains mediates moderates framework mechanism model connects",
            _ => "concept definition framework term idea"
        };

        var top = QaRetrieval.KeywordFallback(chunks, query, 5);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        string topic = childTarget switch
        {
            "core_concept" => "a central concept used in the paper",
            "key_indicator" => "a key measure or indicator used in the paper",
            "concept_connections" => "how the paper connects its main concepts",
            _ => "the paper's key concepts"
        };

        string narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            $"concepts_{childTarget}",
            topic,
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            $"concepts_{childTarget}",
            narrative,
            chunkPreviews
        );

        var choices = childTarget switch
        {
            "core_concept" => new List<TutorChoice>
        {
            new("c4-2", choiceSet.c1, TutorAction.ExploreChildTopic, "key_indicator"),
            new("c4-3", choiceSet.c2, TutorAction.ExploreChildTopic, "concept_connections"),
            new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
        },
            "key_indicator" => new List<TutorChoice>
        {
            new("c4-1", choiceSet.c1, TutorAction.ExploreChildTopic, "core_concept"),
            new("c4-3", choiceSet.c2, TutorAction.ExploreChildTopic, "concept_connections"),
            new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
        },
            "concept_connections" => new List<TutorChoice>
        {
            new("c4-1", choiceSet.c1, TutorAction.ExploreChildTopic, "core_concept"),
            new("c4-2", choiceSet.c2, TutorAction.ExploreChildTopic, "key_indicator"),
            new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
        },
            _ => new List<TutorChoice>
        {
            new("c4-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
        }
        };

        string summary = childTarget switch
        {
            "core_concept" => "Concepts: core concept",
            "key_indicator" => "Concepts: key indicator",
            "concept_connections" => "Concepts: concept connections",
            _ => "Concepts"
        };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: summary
        );
    }

    public static async Task<TutorResponse> BuildAcademicImplicationsOverview(
    TutorSession session,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to introduce the implications section yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("c5-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Implications overview unavailable"
            );
        }

        var query = "implications significance contribution conclusion discussion broader importance limitations applications policy relevance";
        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
            cites = new List<int> { 1 };

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            "implications_overview",
            "overview of the paper's implications",
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            "implications",
            narrative,
            chunkPreviews
        );

        var choices = new List<TutorChoice>
    {
        new("c5-1", choiceSet.c1, TutorAction.ExploreChildTopic, "broader_significance"),
        new("c5-2", choiceSet.c2, TutorAction.ExploreChildTopic, "practical_implications"),
        new("c5-3", choiceSet.c3, TutorAction.ExploreChildTopic, "limits_of_interpretation"),
        new(
            "c5-4",
            FocusMenuLabel(),
            TutorAction.ChangeFocus,
            "focus_menu"
        )
    };

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: "Entered implications focus"
        );
    }
    public static async Task<TutorResponse> BuildAcademicImplicationsResponse(
    TutorSession session,
    string childTarget,
    ChatClient chat)
    {
        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to guide this implications step yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another focus. [p:1]",
                Choices: new List<TutorChoice>
                {
                new("imp-back", ReturnToFocusLabel("implications"), TutorAction.ChangeFocus, "implications")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Implications retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "broader_significance" => "contribution significance discussion conclusion interpretation broader importance",
            "practical_implications" => "applications relevance practice policy decisions recommendations real world impact",
            "limits_of_interpretation" => "limitations constraints caution generalizability scope assumptions validity",
            _ => "discussion conclusion implications interpretation"
        };

        var top = QaRetrieval.KeywordFallback(chunks, query, 5);

        var chosen = top
            .OrderByDescending(x =>
                x.Score +
                (x.Preview.Contains("conclusion", StringComparison.OrdinalIgnoreCase) ? 0.5 : 0) +
                (x.Preview.Contains("discussion", StringComparison.OrdinalIgnoreCase) ? 0.5 : 0) +
                (x.Preview.Contains("implication", StringComparison.OrdinalIgnoreCase) ? 0.7 : 0)
            )
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();

        string topic = childTarget switch
        {
            "broader_significance" => "broader significance",
            "practical_implications" => "practical or policy implications",
            "limits_of_interpretation" => "limits of interpretation",
            _ => "implications"
        };

        string narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            $"implications_{childTarget}",
            topic,
            chunkPreviews,
            cites
        );

        var choiceSet = await TutorChoiceWriter.GenerateChoicesAsync(
            chat,
            $"implications_{childTarget}",
            narrative,
            chunkPreviews
        );

        var pending = childTarget switch
        {
            "broader_significance" => BuildInitialDrillChoices(session, "implications", childTarget, "field_contribution", "significance_from_findings"),
            "practical_implications" => BuildInitialDrillChoices(session, "implications", childTarget, "real_world_relevance", "policy_decision_impact"),
            "limits_of_interpretation" => BuildInitialDrillChoices(session, "implications", childTarget, "main_constraints", "limits_from_method"),
            _ => new List<TutorDrillNode>()
        };

        var choices = childTarget switch
        {
            "broader_significance" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "field_contribution"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "significance_from_findings"),
            new("c5-1-c", ReturnToFocusLabel("implications"), TutorAction.ChangeFocus, "implications")
        },
            "practical_implications" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "real_world_relevance"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "policy_decision_impact"),
            new("c5-2-c", ReturnToFocusLabel("implications"), TutorAction.ChangeFocus, "implications")
        },
            "limits_of_interpretation" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "main_constraints"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "limits_from_method"),
            new("c5-3-c", ReturnToFocusLabel("implications"), TutorAction.ChangeFocus, "implications")
        },
            _ => new List<TutorChoice>
        {
            new("imp-back", ReturnToFocusLabel("implications"), TutorAction.ChangeFocus, "implications")
        }
        };

        string summary = childTarget switch
        {
            "broader_significance" => "Implications: broader significance",
            "practical_implications" => "Implications: practical implications",
            "limits_of_interpretation" => "Implications: limits of interpretation",
            _ => "Implications"
        };

        SaveInitialDrillChoices(session, pending);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: summary
        );
    }

    public static async Task<TutorResponse> BuildAcademicDrillResponse(
    TutorSession session,
    TutorDrillNode requestedNode,
    string returnChoiceId,
    ChatClient chat)
    {
        const int maxDepth = 3;

        if (!InMemoryStore.VectorIndex.TryGetValue(session.UploadId.ToString(), out var chunks) || chunks.Count == 0)
        {
            return new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "I can’t find enough indexed document content to drill deeper into this topic yet. [p:1]\n\n" +
                    "Try indexing the document first or return to another branch. [p:1]",
                Choices: new List<TutorChoice>
                {
                    new(returnChoiceId, ReturnToFocusLabel(requestedNode.Focus), TutorAction.ChangeFocus, requestedNode.ChildTarget),
                    new($"{FocusPrefix(requestedNode.Focus)}-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Drill retrieval unavailable"
            );
        }

        var path = session.DrillPath is null ? new List<TutorDrillNode>() : new List<TutorDrillNode>(session.DrillPath);
        var depth = requestedNode.Depth <= 0 ? path.Count : requestedNode.Depth;

        if (depth >= maxDepth)
        {
            return BuildRecursiveDrillRecap(session, requestedNode, path);
        }

        var query = string.IsNullOrWhiteSpace(requestedNode.Query)
            ? $"{FocusLabel(requestedNode.Focus)} {ChildLabel(requestedNode.ChildTarget)} {DrillLabel(requestedNode.DrillTarget)} {DrillQueryTerms(requestedNode.DrillTarget)}"
            : requestedNode.Query;

        var top = QaRetrieval.KeywordFallback(chunks, query, 6);

        var chosen = top
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var cites = chosen
            .Select(x => x.Page)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var branchVisited = path
            .SelectMany(x => x.Cites ?? new List<int>())
            .Distinct()
            .ToList();

        var newPages = cites.Where(p => !branchVisited.Contains(p)).ToList();
        if (path.Count > 0 && newPages.Count == 0)
        {
            return BuildRecursiveDrillRecap(session, requestedNode, path);
        }

        var chunkPreviews = chosen.Select(x => x.Preview).ToList();
        var topic = $"{ChildLabel(requestedNode.ChildTarget)}: {DrillLabel(requestedNode.DrillTarget)}";

        var narrative = await TutorAiWriter.GenerateNarrativeAsync(
            chat,
            $"{requestedNode.Focus}_{requestedNode.ChildTarget}_{requestedNode.DrillTarget}",
            topic,
            chunkPreviews,
            cites
        );

        var completedNode = requestedNode with
        {
            Query = query,
            Cites = cites,
            Summary = $"Drill: {topic}",
            Depth = depth
        };

        path.Add(completedNode);

        var pending = BuildNextDrillChoices(completedNode, path, chunkPreviews);
        if (pending.Count == 0)
        {
            return BuildRecursiveDrillRecap(
                session with
                {
                    DrillPath = path,
                    PendingDrillChoices = new List<TutorDrillNode>(),
                    LastStepSummary = completedNode.Summary
                },
                completedNode,
                path);
        }

        TutorSessionStore.Sessions[session.SessionId] = session with
        {
            DrillPath = path,
            PendingDrillChoices = pending,
            LastStepSummary = completedNode.Summary
        };

        var choices = new List<TutorChoice>();
        for (var i = 0; i < pending.Count; i++)
        {
            choices.Add(new($"drill:{i}", BuildDrillChoiceLabel(pending[i]), TutorAction.DrillDeeper, pending[i].DrillTarget));
        }
        choices.Add(new(returnChoiceId, ReturnToFocusLabel(requestedNode.Focus), TutorAction.ChangeFocus, requestedNode.ChildTarget));
        choices.Add(new($"{FocusPrefix(requestedNode.Focus)}-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu"));

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative: narrative,
            Choices: choices,
            Cites: cites,
            StepSummary: $"Drill: {topic}"
        );
    }

    private static List<TutorDrillNode> BuildNextDrillChoices(TutorDrillNode current, List<TutorDrillNode> path, List<string> chunkPreviews)
    {
        if (path.Count >= 3)
        {
            return new List<TutorDrillNode>();
        }

        var nextDepth = path.Count;
        var used = path.Select(x => x.DrillTarget).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = BuildNextDrillCandidates(current)
            .Where(x => !used.Contains(x))
            .ToArray();

        if (candidates.Length == 0)
        {
            return new List<TutorDrillNode>();
        }

        var supported = candidates
            .Select(x => new
            {
                Target = x,
                Score = ScoreDrillChoiceSupport(x, chunkPreviews)
            })
            .Where(x => x.Score >= 2)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Target)
            .ToList();

        var choiceCount = supported.Count >= 2 ? 2 : supported.Count;
        if (supported.Count >= 3 && supported[2].Score >= 3 && supported[2].Score >= supported[1].Score - 1)
        {
            choiceCount = 3;
        }

        return supported
            .Take(choiceCount)
            .Select(x => new TutorDrillNode(
                current.Focus,
                current.ChildTarget,
                x.Target,
                $"{FocusLabel(current.Focus)} {ChildLabel(current.ChildTarget)} {DrillLabel(x.Target)} {DrillQueryTerms(x.Target)}",
                new List<int>(),
                "",
                nextDepth))
            .ToList();
    }

    private static IEnumerable<string> BuildNextDrillCandidates(TutorDrillNode current)
    {
        if (current.Focus == "findings")
        {
            if (current.ChildTarget == "supporting_evidence")
            {
                return current.DrillTarget switch
                {
                    "read_the_result" => new[] { "evidence_to_mechanism", "evidence_strength", "evidence_tradeoff" },
                    "connect_evidence_to_claim" => new[] { "claim_support_strength", "evidence_tradeoff", "evidence_boundary" },
                    _ => new[] { "evidence_to_mechanism", "evidence_strength", "evidence_boundary" }
                };
            }

            if (current.ChildTarget == "why_it_matters")
            {
                return current.DrillTarget switch
                {
                    "argument_significance" => new[] { "contribution_to_field", "why_previous_models_matter", "practical_significance" },
                    "field_or_practice_impact" => new[] { "practical_significance", "scope_of_impact", "adoption_conditions" },
                    _ => new[] { "contribution_to_field", "practical_significance", "scope_of_impact" }
                };
            }

            if (current.ChildTarget == "limits_or_tradeoffs")
            {
                return current.DrillTarget switch
                {
                    "main_tradeoff" => new[] { "tradeoff_evidence", "unsettled_question", "scope_boundary" },
                    "boundary_condition" => new[] { "scope_boundary", "missing_evidence", "unsettled_question" },
                    _ => new[] { "tradeoff_evidence", "scope_boundary", "missing_evidence" }
                };
            }
        }

        var baseTarget = StripDrillAngleSuffix(current.DrillTarget);
        return new[]
        {
            $"{baseTarget}_evidence",
            $"{baseTarget}_limits",
            $"{baseTarget}_significance",
            $"{current.ChildTarget}_connection"
        };
    }

    private static int ScoreDrillChoiceSupport(string drillTarget, List<string> chunkPreviews)
    {
        if (chunkPreviews.Count == 0)
        {
            return 0;
        }

        var text = string.Join(" ", chunkPreviews).ToLowerInvariant();
        var terms = Tokenize($"{DrillLabel(drillTarget)} {DrillQueryTerms(drillTarget)}")
            .Where(t => t.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return terms.Count(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]{2,}")
            .Select(m => m.Value);
    }

    private static List<TutorDrillNode> BuildInitialDrillChoices(TutorSession session, string focus, string childTarget, string firstTarget, string secondTarget)
    {
        return new List<TutorDrillNode>
        {
            new(focus, childTarget, firstTarget, "", new List<int>(), "", 0),
            new(focus, childTarget, secondTarget, "", new List<int>(), "", 0)
        };
    }

    private static void SaveInitialDrillChoices(TutorSession session, List<TutorDrillNode> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        TutorSessionStore.Sessions[session.SessionId] = session with
        {
            DrillPath = new List<TutorDrillNode>(),
            PendingDrillChoices = pending
        };
    }

    private static string DrillChoiceLabel(string drillTarget)
    {
        var baseTarget = StripDrillAngleSuffix(drillTarget);
        var baseLabel = CleanChoiceLabel(DrillLabel(baseTarget));

        if (drillTarget.EndsWith("_evidence", StringComparison.OrdinalIgnoreCase))
            return baseLabel.Contains("evidence", StringComparison.OrdinalIgnoreCase)
                ? $"supporting details for {baseLabel}"
                : $"supporting evidence for {baseLabel}";
        if (drillTarget.EndsWith("_limits", StringComparison.OrdinalIgnoreCase))
            return baseLabel.Contains("limit", StringComparison.OrdinalIgnoreCase)
                ? $"the boundary around {baseLabel}"
                : $"limits of {baseLabel}";
        if (drillTarget.EndsWith("_significance", StringComparison.OrdinalIgnoreCase))
            return baseLabel.Contains("significance", StringComparison.OrdinalIgnoreCase)
                ? $"why {baseLabel} matters"
                : $"the significance of {baseLabel}";
        if (drillTarget.EndsWith("_connection", StringComparison.OrdinalIgnoreCase))
            return baseLabel.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   baseLabel.Contains("relationship", StringComparison.OrdinalIgnoreCase)
                ? $"related details around {baseLabel}"
                : $"connections around {baseLabel}";

        return CleanChoiceLabel(DrillLabel(drillTarget));
    }

    private static string BuildDrillChoiceLabel(TutorDrillNode node)
    {
        var title = node.DrillTarget switch
        {
            var x when x.EndsWith("_evidence", StringComparison.OrdinalIgnoreCase) => "Examine the supporting evidence",
            var x when x.EndsWith("_limits", StringComparison.OrdinalIgnoreCase) => "Test the limitation",
            var x when x.EndsWith("_significance", StringComparison.OrdinalIgnoreCase) => "Connect why it matters",
            var x when x.EndsWith("_connection", StringComparison.OrdinalIgnoreCase) => "Follow the connection",
            "interpret_metrics" => "Compare the measures",
            "read_the_result" => "Read the main result",
            "connect_evidence_to_claim" => "Link evidence to claim",
            "relationship_detail" => "Trace the relationship",
            "argument_significance" => "Connect the result to the argument",
            "field_or_practice_impact" => "Look beyond the paper",
            "exception_detail" => "Look at the exception",
            "exception_vs_trend" => "Compare exception and trend",
            "main_tradeoff" => "Test the main trade-off",
            "boundary_condition" => "Check the boundary",
            "dataset_breadth" => "Inspect the evidence base",
            "source_credibility" => "Question the source strength",
            "variable_definition" => "Clarify the key measure",
            "measurement_choices" => "Inspect the measurement choice",
            "main_metrics" => "Compare the main metrics",
            "comparison_strategy" => "Trace the comparison strategy",
            "problem_detail" => "Define the research problem",
            "why_problem_matters" => "Explain why the problem matters",
            "prior_work_detail" => "Review the prior work",
            "prior_work_connection" => "Connect prior work to this paper",
            "gap_detail" => "Identify the research gap",
            "contribution_positioning" => "Position the contribution",
            "field_contribution" => "Connect to the field",
            "significance_from_findings" => "Link findings to significance",
            "real_world_relevance" => "Consider real-world relevance",
            "policy_decision_impact" => "Consider practical consequences",
            "main_constraints" => "Test the main constraint",
            "limits_from_method" => "Connect limits to method",
            "concept_definition" => "Clarify the concept",
            "concept_measurement" => "See how it is measured",
            "concept_relationships" => "Connect the key concepts",
            "evidence_to_mechanism" => "Connect evidence to mechanism",
            "evidence_strength" => "Weigh the evidence strength",
            "evidence_tradeoff" => "Check the efficiency trade-off",
            "claim_support_strength" => "Test the claim support",
            "evidence_boundary" => "Find the evidence boundary",
            "contribution_to_field" => "Connect to the field",
            "why_previous_models_matter" => "Compare with earlier models",
            "practical_significance" => "Consider practical significance",
            "scope_of_impact" => "Check the scope of impact",
            "adoption_conditions" => "Consider adoption conditions",
            "tradeoff_evidence" => "Examine the trade-off evidence",
            "unsettled_question" => "Name the unsettled question",
            "scope_boundary" => "Check the scope boundary",
            "missing_evidence" => "Look for missing evidence",
            _ => ToTitleCase(DrillChoiceLabel(node.DrillTarget))
        };

        var promise = node.DrillTarget switch
        {
            var x when x.EndsWith("_evidence", StringComparison.OrdinalIgnoreCase) => $"Trace the document evidence behind {ChildLabel(node.ChildTarget)}.",
            var x when x.EndsWith("_limits", StringComparison.OrdinalIgnoreCase) => $"Identify what {ChildLabel(node.ChildTarget)} still does not prove.",
            var x when x.EndsWith("_significance", StringComparison.OrdinalIgnoreCase) => $"Connect {ChildLabel(node.ChildTarget)} to the paper's larger argument.",
            var x when x.EndsWith("_connection", StringComparison.OrdinalIgnoreCase) => $"Follow how {ChildLabel(node.ChildTarget)} links to the larger claim.",
            "interpret_metrics" => "See which measurements decide the strength of the findings.",
            "read_the_result" => "See exactly what evidence supports the claim.",
            "connect_evidence_to_claim" => "Walk from the reported evidence back to the central claim.",
            "relationship_detail" => "See how the results or concepts depend on one another.",
            "argument_significance" => "Understand why this result changes the paper's overall argument.",
            "field_or_practice_impact" => "Consider why this finding matters for the field or practice.",
            "exception_detail" => "Find the case that complicates the main pattern.",
            "exception_vs_trend" => "See whether the exception weakens or sharpens the trend.",
            "main_tradeoff" => "See what the result gains and what it may give up.",
            "boundary_condition" => "Look at where the finding may not fully apply.",
            "evidence_to_mechanism" => "See how the results support the paper's explanation.",
            "evidence_strength" => "Judge how strongly the reported results support the claim.",
            "evidence_tradeoff" => "See what the efficiency gains cost or leave unresolved.",
            "claim_support_strength" => "Test whether the evidence fully supports the headline claim.",
            "evidence_boundary" => "Find where the reported evidence stops being decisive.",
            "contribution_to_field" => "See how the finding changes the broader research direction.",
            "why_previous_models_matter" => "Understand why the baselines make the result persuasive.",
            "practical_significance" => "Connect the finding to modeling or deployment choices.",
            "scope_of_impact" => "See how far the finding can reasonably travel.",
            "adoption_conditions" => "Identify what would need to hold for the result to transfer.",
            "tradeoff_evidence" => "Look at the evidence for gains, costs, and compromises.",
            "unsettled_question" => "Name what remains unclear after the reported result.",
            "scope_boundary" => "Find where the paper's evidence may stop applying.",
            "missing_evidence" => "Look for evidence the paper would need to be stronger.",
            _ => $"Go one step deeper into {ChildLabel(node.ChildTarget)}."
        };

        return $"{title}\n{promise}";
    }

    private static string ReturnToFocusLabel(string focus)
    {
        return $"Back to {FocusLabel(focus)}\nReturn to the main {FocusLabel(focus)} options.";
    }

    private static string FocusMenuLabel()
    {
        return "Change focus\nReturn to the main guide menu.";
    }

    private static string StripDrillAngleSuffix(string drillTarget)
    {
        foreach (var suffix in new[] { "_evidence", "_limits", "_significance", "_connection" })
        {
            if (drillTarget.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return drillTarget[..^suffix.Length];
            }
        }

        return drillTarget;
    }

    private static string CleanChoiceLabel(string label)
    {
        return Regex.Replace(label, @"\b(\w+)\s+\1\b", "$1", RegexOptions.IgnoreCase);
    }

    private static string ToTitleCase(string label)
    {
        var words = Regex.Matches(label.Replace('_', ' ').ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Take(7)
            .ToList();

        if (words.Count == 0)
        {
            return "Explore the next idea";
        }

        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static TutorResponse BuildRecursiveDrillRecap(TutorSession session, TutorDrillNode current, List<TutorDrillNode> path)
    {
        var cites = path
            .SelectMany(x => x.Cites ?? new List<int>())
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        TutorSessionStore.Sessions[session.SessionId] = session with
        {
            PendingDrillChoices = new List<TutorDrillNode>(),
            LastStepSummary = $"Recap: {ChildLabel(current.ChildTarget)}"
        };

        var covered = BuildDrillRecapSummary(current, path);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative:
                $"Quick recap: this {ChildLabel(current.ChildTarget)} path covered {covered}. [p:{cites[0]}]\n\n" +
                $"The later steps kept returning to the same core evidence rather than opening a clearly new branch, so this is a good point to consolidate before choosing another direction. [p:{cites[^1]}]",
            Choices: new List<TutorChoice>
            {
                new($"{FocusPrefix(current.Focus)}-4", FocusMenuLabel(), TutorAction.ChangeFocus, "focus_menu")
            },
            Cites: cites.Count <= 3 ? cites : new List<int> { cites[0], cites[cites.Count / 2], cites[^1] },
            StepSummary: $"Recap: {ChildLabel(current.ChildTarget)}",
            Stage: "recap"
        );
    }

    private static string BuildDrillRecapSummary(TutorDrillNode current, List<TutorDrillNode> path)
    {
        var labels = path
            .Select(x => StripDrillAngleSuffix(x.DrillTarget).Replace('_', ' '))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(3)
            .ToList();

        if (labels.Count == 0)
        {
            labels.Add(ChildLabel(current.ChildTarget));
        }

        if (labels.Count == 1)
        {
            return labels[0];
        }

        if (labels.Count == 2)
        {
            return $"{labels[0]} and {labels[1]}";
        }

        return string.Join(", ", labels.Take(labels.Count - 1)) + $", and {labels[^1]}";
    }

    private static string FocusPrefix(string focus) => focus switch
    {
        "findings" => "c1",
        "methodology" => "c2",
        "background" => "c3",
        "concepts" => "c4",
        "implications" => "c5",
        _ => "c3"
    };

    private static string FocusLabel(string focus) => focus.Replace('_', ' ');
    private static string ChildLabel(string childTarget) => childTarget.Replace('_', ' ');
    private static string DrillLabel(string drillTarget) => drillTarget.Replace('_', ' ');

    private static string DrillQueryTerms(string drillTarget) => drillTarget switch
    {
        "interpret_metrics" => "metric measure result value pattern interpretation reported evidence",
        "read_the_result" => "main result finding evidence table reported outcome support claim",
        "connect_evidence_to_claim" => "evidence supports claim finding conclusion argument shows demonstrates",
        "relationship_detail" => "relationship association connection interaction between variables results",
        "argument_significance" => "significance implication importance supports argument conclusion contribution",
        "field_or_practice_impact" => "field contribution practice impact application significance broader importance",
        "exception_detail" => "exception contrast unexpected result boundary case qualification",
        "exception_vs_trend" => "exception compared with trend pattern contrast main result",
        "main_tradeoff" => "tradeoff cost benefit limitation efficiency accuracy constraint compromise",
        "boundary_condition" => "boundary condition limitation scope generalizability caveat where applies",
        "dataset_breadth" => "sample dataset source coverage scope cases observations data",
        "source_credibility" => "source reliability validity provenance selection limitation evidence",
        "variable_definition" => "variable definition measure construct operationalization category",
        "measurement_choices" => "measurement choice indicator proxy coding scale operationalized",
        "main_metrics" => "analysis metric model comparison estimate result method",
        "comparison_strategy" => "comparison baseline control group method design strategy",
        "problem_detail" => "problem question issue motivation challenge addressed",
        "why_problem_matters" => "importance motivation significance why matters gap stakes",
        "prior_work_detail" => "prior work literature previous research earlier studies",
        "prior_work_connection" => "builds on extends contrasts prior work literature connection",
        "gap_detail" => "gap limitation missing unresolved weakness prior research",
        "contribution_positioning" => "contribution addresses gap positions paper advances literature",
        "field_contribution" => "contribution field literature significance advances understanding",
        "significance_from_findings" => "findings imply significance conclusion contribution result",
        "real_world_relevance" => "application practice relevance real world use impact",
        "policy_decision_impact" => "policy decision recommendation practical implication action",
        "main_constraints" => "limitation constraint scope caution validity generalizability",
        "limits_from_method" => "method limitation data constraint design assumption validity",
        "concept_definition" => "defines definition meaning term construct category distinction describes understood as",
        "concept_measurement" => "measured by operationalized indicator variable metric proxy coding scale index",
        "concept_relationships" => "relationship between linked to associated with connects framework mechanism model",
        "evidence_to_mechanism" => "mechanism explanation evidence supports claim result finding process method",
        "evidence_strength" => "strong evidence support result significant comparison table pattern finding",
        "evidence_tradeoff" => "tradeoff cost benefit limitation accuracy efficiency constraint compromise",
        "claim_support_strength" => "supports claim evidence enough result proves demonstrates comparison",
        "evidence_boundary" => "boundary limitation evidence scope sample context dataset generalize caveat",
        "contribution_to_field" => "contribution field research direction advances state of art significance",
        "why_previous_models_matter" => "previous model baseline comparison recurrent convolutional prior work state of art",
        "practical_significance" => "practical deployment training cost efficiency speed resource application",
        "scope_of_impact" => "scope impact generalize context domain application population setting",
        "adoption_conditions" => "condition adoption transfer requirement data method context setting",
        "tradeoff_evidence" => "tradeoff evidence cost benefit efficiency accuracy limitation result",
        "unsettled_question" => "unresolved unclear future work limitation question remains evidence",
        "scope_boundary" => "scope boundary limitation applies generalization dataset context population",
        "missing_evidence" => "missing evidence limitation absent not shown future work additional experiment",
        var x when x.EndsWith("_evidence", StringComparison.OrdinalIgnoreCase) => "evidence example passage result support detail",
        var x when x.EndsWith("_limits", StringComparison.OrdinalIgnoreCase) => "limit constraint caveat uncertainty scope qualification",
        var x when x.EndsWith("_significance", StringComparison.OrdinalIgnoreCase) => "significance implication importance contribution why matters",
        var x when x.EndsWith("_connection", StringComparison.OrdinalIgnoreCase) => "connection relationship links builds on explains relates",
        _ => "detail evidence explanation significance"
    };
}
