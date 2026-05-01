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
                new("c3-4", "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction", TutorAction.ChangeFocus, "focus_menu")
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
            "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction",
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
                new("background-back", "Return to background.\nGo back to the main background branches.\n→ return to background", TutorAction.ChangeFocus, "background")
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
            new("c3-1-c", "Return to background.\nGo back to the main background branches.\n→ return to background", TutorAction.ChangeFocus, "background")
        },
            "prior_work" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "prior_work_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "prior_work_connection"),
            new("c3-2-c", "Return to background.\nGo back to the main background branches.\n→ return to background", TutorAction.ChangeFocus, "background")
        },
            "research_gap" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "gap_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "contribution_positioning"),
            new("c3-3-c", "Return to background.\nGo back to the main background branches.\n→ return to background", TutorAction.ChangeFocus, "background")
        },
            _ => new List<TutorChoice>
        {
            new("background-back", "Return to background.\nGo back to the main background branches.\n→ return to background", TutorAction.ChangeFocus, "background")
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
                new("c2-4", "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction", TutorAction.ChangeFocus, "focus_menu")
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
            "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction",
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
                new("method-back", "Return to methodology.\nGo back to the main methodology branches.\n→ return to methodology", TutorAction.ChangeFocus, "methodology")
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
            new("c2-1-c", "Return to methodology.\nGo back to the main methodology branches.\n→ return to methodology", TutorAction.ChangeFocus, "methodology")
        },
            "measures" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "variable_definition"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "measurement_choices"),
            new("c2-2-c", "Return to methodology.\nGo back to the main methodology branches.\n→ return to methodology", TutorAction.ChangeFocus, "methodology")
        },
            "analysis_methods" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "main_metrics"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "comparison_strategy"),
            new("c2-3-c", "Return to methodology.\nGo back to the main methodology branches.\n→ return to methodology", TutorAction.ChangeFocus, "methodology")
        },
            _ => new List<TutorChoice>
        {
            new("method-back", "Return to methodology.\nGo back to the main methodology branches.\n→ return to methodology", TutorAction.ChangeFocus, "methodology")
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
        new("c1-1", choiceSet.c1, TutorAction.ExploreChildTopic, "measurement"),
        new("c1-2", choiceSet.c2, TutorAction.ExploreChildTopic, "result_relationships"),
        new("c1-3", choiceSet.c3, TutorAction.ExploreChildTopic, "exceptions"),
        new(
            "c1-4",
            "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction",
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
                new("findings-back", "Return to findings.\nGo back to the main findings branches.", TutorAction.ChangeFocus, "findings")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Findings retrieval unavailable"
            );
        }

        string query = childTarget switch
        {
            "measurement" => "findings results evidence reported patterns measures indicators outcomes analysis",
            "result_relationships" => "relationship association connection between main variables outcomes claims findings",
            "exceptions" => "exception contrast boundary case mixed results qualification unexpected finding limitation",
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
            "measurement" => await BuildFindingsMeasurementNarrative(chat, chosen, cites),
            "result_relationships" => await BuildFindingsRelationshipsNarrative(chat, chosen, cites),
            "exceptions" => await BuildFindingsExceptionsNarrative(chat, chosen, cites),
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
            "measurement" => BuildInitialDrillChoices(session, "findings", childTarget, "interpret_metrics", "connect_evidence_to_claim"),
            "result_relationships" => BuildInitialDrillChoices(session, "findings", childTarget, "relationship_detail", "argument_significance"),
            "exceptions" => BuildInitialDrillChoices(session, "findings", childTarget, "exception_detail", "exception_vs_trend"),
            _ => new List<TutorDrillNode>()
        };

        var choices = childTarget switch
        {
            "measurement" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "interpret_metrics"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "connect_evidence_to_claim"),
            new("c1-1-c", "Return to findings.\nGo back to the main findings branches.\n→ return to findings", TutorAction.ChangeFocus, "findings")
        },
            "result_relationships" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "relationship_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "argument_significance"),
            new("c1-2-c", "Return to findings.\nGo back to the main findings branches.\n→ return to findings", TutorAction.ChangeFocus, "findings")
        },
            "exceptions" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "exception_detail"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "exception_vs_trend"),
            new("c1-3-c", "Return to findings.\nGo back to the main findings branches.\n→ return to findings", TutorAction.ChangeFocus, "findings")
        },
            _ => new List<TutorChoice>
        {
            new("findings-back", "Return to findings.\nGo back to the main findings branches.\n→ return to findings", TutorAction.ChangeFocus, "findings")
        }
        };

        string summary = childTarget switch
        {
            "measurement" => "Findings: measurement",
            "result_relationships" => "Findings: relationships",
            "exceptions" => "Findings: exceptions",
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
            "measurement",
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
            "result relationships",
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
            "exceptions",
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
                new("c4-4", "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction", TutorAction.ChangeFocus, "focus_menu")
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
            "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction",
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
                new("c4-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
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
            new("c4-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
        },
            "key_indicator" => new List<TutorChoice>
        {
            new("c4-1", choiceSet.c1, TutorAction.ExploreChildTopic, "core_concept"),
            new("c4-3", choiceSet.c2, TutorAction.ExploreChildTopic, "concept_connections"),
            new("c4-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
        },
            "concept_connections" => new List<TutorChoice>
        {
            new("c4-1", choiceSet.c1, TutorAction.ExploreChildTopic, "core_concept"),
            new("c4-2", choiceSet.c2, TutorAction.ExploreChildTopic, "key_indicator"),
            new("c4-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
        },
            _ => new List<TutorChoice>
        {
            new("c4-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
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
                new("c5-4", "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction", TutorAction.ChangeFocus, "focus_menu")
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
            "Another part of the paper may be more useful right now.\nThis returns to the main focus menu.\n→ choose a different direction",
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
                new("imp-back", "Return to implications.\nGo back to the main implications branches.\n→ return to implications", TutorAction.ChangeFocus, "implications")
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
            new("c5-1-c", "Return to implications.\nGo back to the main implications branches.\n→ return to implications", TutorAction.ChangeFocus, "implications")
        },
            "practical_implications" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "real_world_relevance"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "policy_decision_impact"),
            new("c5-2-c", "Return to implications.\nGo back to the main implications branches.\n→ return to implications", TutorAction.ChangeFocus, "implications")
        },
            "limits_of_interpretation" => new List<TutorChoice>
        {
            new("drill:0", choiceSet.c1, TutorAction.DrillDeeper, "main_constraints"),
            new("drill:1", choiceSet.c2, TutorAction.DrillDeeper, "limits_from_method"),
            new("c5-3-c", "Return to implications.\nGo back to the main implications branches.\n→ return to implications", TutorAction.ChangeFocus, "implications")
        },
            _ => new List<TutorChoice>
        {
            new("imp-back", "Return to implications.\nGo back to the main implications branches.\n→ return to implications", TutorAction.ChangeFocus, "implications")
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
                    new(returnChoiceId, "Return to this branch.\nGo back to the previous tutor options.", TutorAction.ChangeFocus, requestedNode.ChildTarget),
                    new($"{FocusPrefix(requestedNode.Focus)}-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
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
        TutorSessionStore.Sessions[session.SessionId] = session with
        {
            DrillPath = path,
            PendingDrillChoices = pending,
            LastStepSummary = completedNode.Summary
        };

        var choices = new List<TutorChoice>();
        for (var i = 0; i < pending.Count; i++)
        {
            choices.Add(new($"drill:{i}", $"A closer thread appears.\n{DrillChoiceLabel(pending[i].DrillTarget)}...", TutorAction.DrillDeeper, pending[i].DrillTarget));
        }
        choices.Add(new(returnChoiceId, "This branch has another angle.\nBack to the previous options...", TutorAction.ChangeFocus, requestedNode.ChildTarget));
        choices.Add(new($"{FocusPrefix(requestedNode.Focus)}-4", "Another part of the paper may shift the view.\nBack to the wider map...", TutorAction.ChangeFocus, "focus_menu"));

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
        var baseTarget = StripDrillAngleSuffix(current.DrillTarget);
        var candidates = new[]
        {
            $"{baseTarget}_evidence",
            $"{baseTarget}_limits",
            $"{baseTarget}_significance",
            $"{current.ChildTarget}_connection"
        };

        var supported = candidates
            .Where(x => !used.Contains(x))
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

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative:
                $"This branch has reached a natural recap point for {ChildLabel(current.ChildTarget)}. [p:{cites[0]}]\n\n" +
                $"The recent drill steps are no longer surfacing a clearly new supported angle, so this is a good place to consolidate before changing direction. [p:{cites[^1]}]",
            Choices: new List<TutorChoice>
            {
                new($"{FocusPrefix(current.Focus)}-4", "Change focus.\nReturn and choose another part of the paper.", TutorAction.ChangeFocus, "focus_menu")
            },
            Cites: cites.Count <= 3 ? cites : new List<int> { cites[0], cites[cites.Count / 2], cites[^1] },
            StepSummary: $"Recap: {ChildLabel(current.ChildTarget)}"
        );
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
        "connect_evidence_to_claim" => "evidence supports claim finding conclusion argument shows demonstrates",
        "relationship_detail" => "relationship association connection interaction between variables results",
        "argument_significance" => "significance implication importance supports argument conclusion contribution",
        "exception_detail" => "exception contrast unexpected result boundary case qualification",
        "exception_vs_trend" => "exception compared with trend pattern contrast main result",
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
        var x when x.EndsWith("_evidence", StringComparison.OrdinalIgnoreCase) => "evidence example passage result support detail",
        var x when x.EndsWith("_limits", StringComparison.OrdinalIgnoreCase) => "limit constraint caveat uncertainty scope qualification",
        var x when x.EndsWith("_significance", StringComparison.OrdinalIgnoreCase) => "significance implication importance contribution why matters",
        var x when x.EndsWith("_connection", StringComparison.OrdinalIgnoreCase) => "connection relationship links builds on explains relates",
        _ => "detail evidence explanation significance"
    };
}
