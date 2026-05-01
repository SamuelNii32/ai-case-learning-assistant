using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using OpenAI.Chat;

public static class TutorEndpoints
{
    public static void MapTutorEndpoints(this WebApplication app, string connString)
    {
        app.MapPost("/tutor/start/{uploadId:guid}", async (Guid uploadId, TutorStartRequest? request, HttpContext ctx, IWebHostEnvironment env) =>
        {
            var me = (string?)ctx.Items["userId"] ?? ctx.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using (var conn = new SqliteConnection(connString))
            {
                await conn.OpenAsync();

                using var chk = conn.CreateCommand();
                chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
                chk.Parameters.AddWithValue("$u", uploadId.ToString());
                chk.Parameters.AddWithValue("$me", me);

                var ok = await chk.ExecuteScalarAsync();
                if (ok is null)
                {
                    return Results.NotFound(new { error = "not found" });
                }
            }

            DocType category;
            if (DocTypePersistence.TryLoad(uploadId, env, out var docTypeResult) && docTypeResult is not null)
            {
                category = docTypeResult.DocType;
            }
            else
            {
                category = DocType.UnsupportedOther;
            }

            var focus = request?.Focus ?? "overview";
            var sessionId = Guid.NewGuid().ToString("N");

            var session = new TutorSession(
                SessionId: sessionId,
                UploadId: uploadId,
                Category: category,
                Focus: focus,
                CurrentNode: "start",
                VisitedTopics: new List<string>(),
                VisitedPages: new List<int>(),
                History: new List<string>(),
                LastStepSummary: null,
                DrillPath: new List<TutorDrillNode>(),
                PendingDrillChoices: new List<TutorDrillNode>()
            );

            TutorSessionStore.Sessions[sessionId] = session;

            TutorResponse response;

            if (category == DocType.AcademicResearch)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be an academic research paper and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the main findings, the methodology, the theoretical framing, or the key concepts used in the paper. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "The paper’s main findings set the stakes.\nWhat it ultimately claims...", TutorAction.ExploreFocus, "findings"),
                        new("c2", "The method shapes what the study can prove.\nHow the evidence was built...", TutorAction.ExploreFocus, "methodology"),
                        new("c3", "The background explains why this problem matters.\nThe gap behind the paper...", TutorAction.ExploreFocus, "background"),
                        new("c4", "The key concepts carry the argument.\nThe terms doing the real work...", TutorAction.ExploreFocus, "concepts"),
                        new("c5", "The conclusions reach beyond the results.\nWhat follows from them...", TutorAction.ExploreFocus, "implications")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else if (category == DocType.BusinessCase)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be a business case and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the central problem, the available alternatives, the analysis, or the recommendation. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Examine the central problem.\nUnderstand the core challenge facing the decision-maker.", TutorAction.ExploreFocus, "problem"),
                        new("c2", "Review the alternatives.\nCompare the main options presented in the case.", TutorAction.ExploreFocus, "alternatives"),
                        new("c3", "Analyze the evidence.\nLook at the reasoning, data, and trade-offs in the case.", TutorAction.ExploreFocus, "analysis"),
                        new("c4", "Evaluate the recommendation.\nConsider the proposed course of action and its implications.", TutorAction.ExploreFocus, "recommendation")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else if (category == DocType.LegalCase)
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document appears to be a legal case and is suitable for guided analysis. [p:1]\n\n" +
                        "The tutor can begin with the facts, the legal issues, the applicable rules, or the court’s reasoning. [p:1]\n\n" +
                        "Choose a direction to begin the exploration. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Review the facts.\nUnderstand the events and background of the dispute.", TutorAction.ExploreFocus, "facts"),
                        new("c2", "Identify the legal issues.\nClarify the main questions the case is addressing.", TutorAction.ExploreFocus, "issues"),
                        new("c3", "Examine the rules and authorities.\nSee what legal principles or precedents apply.", TutorAction.ExploreFocus, "rules"),
                        new("c4", "Analyze the court’s reasoning.\nUnderstand how the decision is justified.", TutorAction.ExploreFocus, "analysis")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Tutor start"
                );
            }
            else
            {
                response = new TutorResponse(
                    SessionId: sessionId,
                    Narrative:
                        "This document does not match a supported tutor category for guided analysis. [p:1]\n\n" +
                        "Tutor mode currently works best for academic research papers, business cases, and legal cases. [p:1]\n\n" +
                        "Use chat mode for free-form questions about this document. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("c1", "Return to chat mode.\nAsk a direct question about the document instead.", TutorAction.ChangeFocus, "chat")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Unsupported tutor category"
                );
            }

            session = session with
            {
                VisitedPages = response.Cites.Distinct().OrderBy(x => x).ToList(),
                History = new List<string> { response.StepSummary },
                LastStepSummary = response.StepSummary
            };

            TutorSessionStore.Sessions[sessionId] = session;
            await TutorSessionPersistence.SaveAsync(connString, session, me);
            return Results.Json(response);
        });

        app.MapPost("/tutor/step", async (TutorStepRequest request, HttpContext ctx, ChatClient chat) =>
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.SessionId) ||
                string.IsNullOrWhiteSpace(request.ChoiceId))
            {
                return Results.BadRequest(new { error = "Invalid request" });
            }

            if (!TutorSessionStore.Sessions.TryGetValue(request.SessionId, out var session))
            {
                session = await TutorSessionPersistence.TryLoadAsync(connString, request.SessionId);
                if (session is null)
                {
                    return Results.NotFound(new { error = "Tutor session not found" });
                }

                TutorSessionStore.Sessions[session.SessionId] = session;
            }

            var me = (string?)ctx.Items["userId"] ?? ctx.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(me))
            {
                return Results.Unauthorized();
            }

            using (var conn = new SqliteConnection(connString))
            {
                await conn.OpenAsync();

                using var chk = conn.CreateCommand();
                chk.CommandText = @"
SELECT 1
FROM Uploads u
WHERE UPPER(u.UploadId) = UPPER($u)
  AND (
        u.UserId = $me
     OR EXISTS (
            SELECT 1
            FROM ClassCases cc
            JOIN ClassStudents cs ON cs.ClassId = cc.ClassId
            WHERE UPPER(cc.UploadId) = UPPER(u.UploadId)
              AND cs.StudentId = $me
        )
  )
LIMIT 1;
";
                chk.Parameters.AddWithValue("$u", session.UploadId.ToString());
                chk.Parameters.AddWithValue("$me", me);

                var ok = await chk.ExecuteScalarAsync();
                if (ok is null)
                {
                    return Results.Forbid();
                }
            }

            TutorResponse response;

            // TOP-LEVEL ACADEMIC FOCUS HANDLING
            if (session.Category == DocType.AcademicResearch)
            {
                if (request.ChoiceId == "c1")
                {
                    session = session with
                    {
                        Focus = "findings",
                        CurrentNode = "focus:findings",
                        LastStepSummary = "Entered findings focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsOverview(session, chat);
                }

                else if (request.ChoiceId == "c1-1")
                {
                    session = session with
                    {
                        CurrentNode = "findings:measurement",
                        LastStepSummary = "Entered findings measurement"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "measurement", chat);
                }
                else if (request.ChoiceId == "c1-2")
                {
                    session = session with
                    {
                        CurrentNode = "findings:result_relationships",
                        LastStepSummary = "Entered findings relationships"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "result_relationships", chat);
                }
                else if (request.ChoiceId == "c1-3")
                {
                    session = session with
                    {
                        CurrentNode = "findings:exceptions",
                        LastStepSummary = "Entered findings exceptions"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicFindingsResponse(session, "exceptions", chat);
                }
                else if (request.ChoiceId == "c1-4" || request.ChoiceId == "c1-1-c" || request.ChoiceId == "c1-2-c" || request.ChoiceId == "c1-3-c")
                {
                    session = session with
                    {
                        Focus = "findings",
                        CurrentNode = "focus:findings",
                        LastStepSummary = "Returned to findings focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on the paper’s main findings and how they support the overall argument. [p:1]\n\n" +
                            "The next step can examine how the findings are established, how the main results relate to one another, or whether the paper discusses exceptions, contrasts, or boundary cases. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1-1", "The findings depend on particular evidence.\nMetrics, patterns, and support...", TutorAction.ExploreChildTopic, "measurement"),
                            new("c1-2", "The results may be doing more together.\nConnections between claims...", TutorAction.ExploreChildTopic, "result_relationships"),
                            new("c1-3", "Some results may complicate the pattern.\nExceptions and contrasts...", TutorAction.ExploreChildTopic, "exceptions"),
                            new("c1-4", "Another part of the paper may shift the view.\nBack to the wider map...", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to findings focus"
                    );
                }
                else if (request.ChoiceId == "c2")
                {
                    session = session with
                    {
                        Focus = "methodology",
                        CurrentNode = "focus:methodology",
                        LastStepSummary = "Entered methodology focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyOverview(session, chat);
                }
                else if (request.ChoiceId == "c3")
                {
                    session = session with
                    {
                        Focus = "background",
                        CurrentNode = "focus:background",
                        LastStepSummary = "Entered background focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundOverview(session, chat);
                }

                else if (request.ChoiceId == "c3-1")
                {
                    session = session with
                    {
                        CurrentNode = "background:problem_framing",
                        LastStepSummary = "Entered background problem framing"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "problem_framing", chat);
                }
                else if (request.ChoiceId == "c3-2")
                {
                    session = session with
                    {
                        CurrentNode = "background:prior_work",
                        LastStepSummary = "Entered background prior work"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "prior_work", chat);
                }
                else if (request.ChoiceId == "c3-3")
                {
                    session = session with
                    {
                        CurrentNode = "background:research_gap",
                        LastStepSummary = "Entered background research gap"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicBackgroundResponse(session, "research_gap", chat);
                }
                else if (request.ChoiceId == "c4")
                {
                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = "focus:concepts",
                        LastStepSummary = "Entered concepts focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicConceptsOverview(session, chat);
                }
                else if (request.ChoiceId == "c4-1" || request.ChoiceId == "c4-2" || request.ChoiceId == "c4-3")
                {
                    var target = request.ChoiceId switch
                    {
                        "c4-1" => "core_concept",
                        "c4-2" => "key_indicator",
                        "c4-3" => "concept_connections",
                        _ => "core_concept"
                    };

                    var drillTarget = request.ChoiceId switch
                    {
                        "c4-1" => "concept_definition",
                        "c4-2" => "concept_measurement",
                        "c4-3" => "concept_relationships",
                        _ => "concept_definition"
                    };

                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = $"concepts:{target}:{drillTarget}",
                        LastStepSummary = $"Entered concepts drill {drillTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    var drillNode = new TutorDrillNode(
                        Focus: "concepts",
                        ChildTarget: target,
                        DrillTarget: drillTarget,
                        Query: "",
                        Cites: new List<int>(),
                        Summary: "",
                        Depth: session.DrillPath?.Count ?? 0
                    );

                    response = await TutorRetrieval.BuildAcademicDrillResponse(session, drillNode, "concepts-back", chat);
                }
                else if (request.ChoiceId == "concepts-back")
                {
                    session = session with
                    {
                        Focus = "concepts",
                        CurrentNode = "focus:concepts",
                        LastStepSummary = "Returned to concepts focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicConceptsOverview(session, chat);
                }
                else if (request.ChoiceId == "c5")
                {
                    session = session with
                    {
                        Focus = "implications",
                        CurrentNode = "focus:implications",
                        LastStepSummary = "Entered implications focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsOverview(session, chat);
                }

                else if (request.ChoiceId == "c5-1")
                {
                    session = session with
                    {
                        CurrentNode = "implications:broader_significance",
                        LastStepSummary = "Entered implications broader significance"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "broader_significance", chat);
                }
                else if (request.ChoiceId == "c5-2")
                {
                    session = session with
                    {
                        CurrentNode = "implications:practical_implications",
                        LastStepSummary = "Entered implications practical implications"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "practical_implications", chat);
                }
                else if (request.ChoiceId == "c5-3")
                {
                    session = session with
                    {
                        CurrentNode = "implications:limits_of_interpretation",
                        LastStepSummary = "Entered implications limits of interpretation"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicImplicationsResponse(session, "limits_of_interpretation", chat);
                }
                else if (request.ChoiceId == "c5-4" || request.ChoiceId == "c5-1-c" || request.ChoiceId == "c5-2-c" || request.ChoiceId == "c5-3-c")
                {
                    session = session with
                    {
                        Focus = "implications",
                        CurrentNode = "focus:implications",
                        LastStepSummary = "Returned to implications focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on what the paper’s conclusions mean beyond the immediate results. [p:1]\n\n" +
                            "The next step can examine the broader significance of the findings, the practical or policy implications, or the limitations that shape how far the conclusions should be taken. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c5-1", "The contribution reaches into a larger debate.\nWhy the paper matters...", TutorAction.ExploreChildTopic, "broader_significance"),
                            new("c5-2", "The findings may travel beyond the page.\nPractical stakes and policy pressure...", TutorAction.ExploreChildTopic, "practical_implications"),
                            new("c5-3", "The conclusions have boundaries.\nWhere interpretation starts to tighten...", TutorAction.ExploreChildTopic, "limits_of_interpretation"),
                            new("c5-4", "Another part of the paper may shift the view.\nBack to the wider map...", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to implications focus"
                    );
                }

                else if (request.ChoiceId == "c2-1")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:data_sources",
                        LastStepSummary = "Entered methodology data sources"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "data_sources", chat);
                }
                else if (request.ChoiceId == "c2-2")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:measures",
                        LastStepSummary = "Entered methodology measures"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "measures", chat);
                }
                else if (request.ChoiceId == "c2-3")
                {
                    session = session with
                    {
                        CurrentNode = "methodology:analysis_methods",
                        LastStepSummary = "Entered methodology analysis methods"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicMethodologyResponse(session, "analysis_methods", chat);
                }
                else if (request.ChoiceId == "c2-4" || request.ChoiceId == "c2-1-c" || request.ChoiceId == "c2-2-c" || request.ChoiceId == "c2-3-c")
                {
                    session = session with
                    {
                        Focus = "methodology",
                        CurrentNode = "focus:methodology",
                        LastStepSummary = "Returned to methodology focus"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This path focuses on how the study was designed and how the evidence was produced. [p:1]\n\n" +
                            "The next step can examine the data or source material, the way key concepts are measured, or the analytical approach used to interpret the evidence. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c2-1", "The evidence base sets the limits.\nData, sources, and scope...", TutorAction.ExploreChildTopic, "data_sources"),
                            new("c2-2", "The concepts become measurable choices.\nWhere ideas turn into variables...", TutorAction.ExploreChildTopic, "measures"),
                            new("c2-3", "The analysis carries the conclusion.\nComparisons, models, and reasoning...", TutorAction.ExploreChildTopic, "analysis_methods"),
                            new("c2-4", "Another part of the paper may shift the view.\nBack to the wider map...", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to methodology focus"
                    );
                }
                else if (request.ChoiceId == "drill:return" &&
                    TryResolveAcademicDrillReturn(session, out var returnFocus, out var returnChildTarget))
                {
                    session = session with
                    {
                        Focus = returnFocus,
                        CurrentNode = $"{returnFocus}:{returnChildTarget}",
                        LastStepSummary = $"Returned to {returnFocus} {returnChildTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await BuildAcademicDrillReturnResponse(session, returnFocus, returnChildTarget, chat);
                }
                else if (TryResolveAcademicDrillChoice(session, request.ChoiceId, out var drillNode, out var returnChoiceId))
                {
                    session = session with
                    {
                        Focus = drillNode.Focus,
                        CurrentNode = $"{drillNode.Focus}:{drillNode.ChildTarget}:{drillNode.DrillTarget}",
                        LastStepSummary = $"Entered {drillNode.Focus} drill {drillNode.DrillTarget}"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;
                    response = await TutorRetrieval.BuildAcademicDrillResponse(session, drillNode, returnChoiceId, chat);
                }
                else if (request.ChoiceId == "c3-4" || request.ChoiceId == "c4-4" || request.ChoiceId == "back-academic")
                {
                    session = session with
                    {
                        CurrentNode = "focus_menu",
                        LastStepSummary = "Returned to academic focus menu"
                    };

                    TutorSessionStore.Sessions[session.SessionId] = session;

                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "The tutor has returned to the main focus menu for this academic paper. [p:1]\n\n" +
                            "Choose another direction to continue the guided analysis. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "The paper’s main findings set the stakes.\nWhat it ultimately claims...", TutorAction.ExploreFocus, "findings"),
                            new("c2", "The method shapes what the study can prove.\nHow the evidence was built...", TutorAction.ExploreFocus, "methodology"),
                            new("c3", "The theoretical frame shapes the argument.\nThe ideas underneath...", TutorAction.ExploreFocus, "theory"),
                            new("c4", "The key concepts carry the argument.\nThe terms doing the real work...", TutorAction.ExploreFocus, "concepts"),
                            new("c5", "The conclusions reach beyond the results.\nWhat follows from them...", TutorAction.ExploreFocus, "implications")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to academic focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "That tutor choice is not available from the current academic branch. [p:1]\n\n" +
                            "Return to the academic focus menu and choose another supported direction. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("back-academic", "The wider paper is still open.\nBack to the main map...", TutorAction.ChangeFocus, "focus_menu")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Unsupported academic tutor choice"
                    );
                }
            }
            else if (session.Category == DocType.BusinessCase)
            {
                if (request.ChoiceId == "biz-back" || request.ChoiceId == "biz-recap")
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This document appears to be a business case and is suitable for guided analysis. [p:1]\n\n" +
                            "The tutor can begin with the central problem, the available alternatives, the analysis, or the recommendation. [p:1]\n\n" +
                            "Choose a direction to begin the exploration. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "Examine the central problem.\nUnderstand the core challenge facing the decision-maker.", TutorAction.ExploreFocus, "problem"),
                            new("c2", "Review the alternatives.\nCompare the main options presented in the case.", TutorAction.ExploreFocus, "alternatives"),
                            new("c3", "Analyze the evidence.\nLook at the reasoning, data, and trade-offs in the case.", TutorAction.ExploreFocus, "analysis"),
                            new("c4", "Evaluate the recommendation.\nConsider the proposed course of action and its implications.", TutorAction.ExploreFocus, "recommendation")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to business focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "Business case tutor branching has not been implemented yet, but category-aware start is working. [p:1]\n\n" +
                            "The next stage will add problem, alternatives, analysis, and recommendation flows. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("biz-back", "Return to the business focus menu.\nGo back to the top-level business case choices.", TutorAction.ChangeFocus, "focus_menu"),
                            new("biz-recap", "Trigger a recap.\nPreview how recap responses will work.", TutorAction.Recap, "recap")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Business case placeholder step"
                    );
                }
            }
            else if (session.Category == DocType.LegalCase)
            {
                if (request.ChoiceId == "legal-back" || request.ChoiceId == "legal-recap")
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "This document appears to be a legal case and is suitable for guided analysis. [p:1]\n\n" +
                            "The tutor can begin with the facts, the legal issues, the applicable rules, or the court’s reasoning. [p:1]\n\n" +
                            "Choose a direction to begin the exploration. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("c1", "Review the facts.\nUnderstand the events and background of the dispute.", TutorAction.ExploreFocus, "facts"),
                            new("c2", "Identify the legal issues.\nClarify the main questions the case is addressing.", TutorAction.ExploreFocus, "issues"),
                            new("c3", "Examine the rules and authorities.\nSee what legal principles or precedents apply.", TutorAction.ExploreFocus, "rules"),
                            new("c4", "Analyze the court’s reasoning.\nUnderstand how the decision is justified.", TutorAction.ExploreFocus, "analysis")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Returned to legal focus menu"
                    );
                }
                else
                {
                    response = new TutorResponse(
                        SessionId: session.SessionId,
                        Narrative:
                            "Legal case tutor branching has not been implemented yet, but category-aware start is working. [p:1]\n\n" +
                            "The next stage will add facts, issues, rules, and analysis flows. [p:1]",
                        Choices: new List<TutorChoice>
                        {
                            new("legal-back", "Return to the legal focus menu.\nGo back to the top-level legal case choices.", TutorAction.ChangeFocus, "focus_menu"),
                            new("legal-recap", "Trigger a recap.\nPreview how recap responses will work.", TutorAction.Recap, "recap")
                        },
                        Cites: new List<int> { 1 },
                        StepSummary: "Legal case placeholder step"
                    );
                }
            }
            else
            {
                response = new TutorResponse(
                    SessionId: session.SessionId,
                    Narrative:
                        "Tutor mode is not available for this document category. [p:1]\n\n" +
                        "Use chat mode for direct questions instead. [p:1]",
                    Choices: new List<TutorChoice>
                    {
                        new("unsupported-chat", "Return to chat mode.\nAsk a direct question about the document instead.", TutorAction.ChangeFocus, "chat")
                    },
                    Cites: new List<int> { 1 },
                    StepSummary: "Unsupported tutor category"
                );
            }

            if (TutorSessionStore.Sessions.TryGetValue(session.SessionId, out var latestSession))
            {
                session = latestSession;
            }

            var isRecursiveDrill = IsAcademicRecursiveDrillChoice(request.ChoiceId);
            var isFirstLevelChildChoice = IsAcademicFirstLevelChildChoice(request.ChoiceId);
            var allowRecap =
                request.ChoiceId.Contains('-') &&
                !request.ChoiceId.EndsWith("-4", StringComparison.OrdinalIgnoreCase) &&
                !request.ChoiceId.EndsWith("-c", StringComparison.OrdinalIgnoreCase) &&
                !isFirstLevelChildChoice &&
                !isRecursiveDrill;

            if (session.Category == DocType.AcademicResearch && !isRecursiveDrill)
            {
                response = TutorRecap.FinalizeAcademicStep(session, response, allowRecap);
            }
            else if (session.Category != DocType.AcademicResearch)
            {
                TutorSessionStore.Sessions[session.SessionId] = session;
            }

            var sessionToPersist = TutorSessionStore.Sessions.TryGetValue(session.SessionId, out var cachedSession)
                ? cachedSession
                : session;
            await TutorSessionPersistence.SaveAsync(connString, sessionToPersist, me);

            return Results.Json(response);
        });
    }

    private static bool TryResolveAcademicDrillReturn(
        TutorSession session,
        out string focus,
        out string childTarget)
    {
        focus = "";
        childTarget = "";

        var path = session.DrillPath ?? new List<TutorDrillNode>();
        if (path.Count == 0)
        {
            return false;
        }

        var lastNode = path[^1];
        focus = lastNode.Focus;
        childTarget = lastNode.ChildTarget;

        return !string.IsNullOrWhiteSpace(childTarget) &&
            focus is "findings" or "methodology" or "background" or "concepts" or "implications";
    }

    private static async Task<TutorResponse> BuildAcademicDrillReturnResponse(
        TutorSession session,
        string focus,
        string childTarget,
        ChatClient chat)
    {
        return focus switch
        {
            "findings" => await TutorRetrieval.BuildAcademicFindingsResponse(session, childTarget, chat),
            "methodology" => await TutorRetrieval.BuildAcademicMethodologyResponse(session, childTarget, chat),
            "background" => await TutorRetrieval.BuildAcademicBackgroundResponse(session, childTarget, chat),
            "concepts" => await TutorRetrieval.BuildAcademicConceptsResponse(session, childTarget, chat),
            "implications" => await TutorRetrieval.BuildAcademicImplicationsResponse(session, childTarget, chat),
            _ => new TutorResponse(
                SessionId: session.SessionId,
                Narrative:
                    "That tutor drill return is not available from the current academic branch. [p:1]\n\n" +
                    "Return to the academic focus menu and choose another supported direction. [p:1]",
                Choices: new List<TutorChoice>
                {
                    new("back-academic", "The wider paper is still open.\nBack to the main map...", TutorAction.ChangeFocus, "focus_menu")
                },
                Cites: new List<int> { 1 },
                StepSummary: "Unsupported academic drill return"
            )
        };
    }

    private static bool TryResolveAcademicDrillChoice(
        TutorSession session,
        string choiceId,
        out TutorDrillNode drillNode,
        out string returnChoiceId)
    {
        drillNode = null!;
        returnChoiceId = "";

        if (choiceId.StartsWith("drill:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(choiceId["drill:".Length..], out var index))
            {
                var pending = session.PendingDrillChoices ?? new List<TutorDrillNode>();
                if (index >= 0 && index < pending.Count)
                {
                    drillNode = pending[index];
                    returnChoiceId = "drill:return";
                    return true;
                }
            }

            return false;
        }

        (var focus, var childTarget, var drillTarget, returnChoiceId) = choiceId switch
        {
            "c1-1-a" => ("findings", "measurement", "interpret_metrics", "c1-1-c"),
            "c1-1-b" => ("findings", "measurement", "connect_evidence_to_claim", "c1-1-c"),
            "c1-2-a" => ("findings", "result_relationships", "relationship_detail", "c1-2-c"),
            "c1-2-b" => ("findings", "result_relationships", "argument_significance", "c1-2-c"),
            "c1-3-a" => ("findings", "exceptions", "exception_detail", "c1-3-c"),
            "c1-3-b" => ("findings", "exceptions", "exception_vs_trend", "c1-3-c"),

            "c2-1-a" => ("methodology", "data_sources", "dataset_breadth", "c2-1-c"),
            "c2-1-b" => ("methodology", "data_sources", "source_credibility", "c2-1-c"),
            "c2-2-a" => ("methodology", "measures", "variable_definition", "c2-2-c"),
            "c2-2-b" => ("methodology", "measures", "measurement_choices", "c2-2-c"),
            "c2-3-a" => ("methodology", "analysis_methods", "main_metrics", "c2-3-c"),
            "c2-3-b" => ("methodology", "analysis_methods", "comparison_strategy", "c2-3-c"),

            "c3-1-a" => ("background", "problem_framing", "problem_detail", "c3-1-c"),
            "c3-1-b" => ("background", "problem_framing", "why_problem_matters", "c3-1-c"),
            "c3-2-a" => ("background", "prior_work", "prior_work_detail", "c3-2-c"),
            "c3-2-b" => ("background", "prior_work", "prior_work_connection", "c3-2-c"),
            "c3-3-a" => ("background", "research_gap", "gap_detail", "c3-3-c"),
            "c3-3-b" => ("background", "research_gap", "contribution_positioning", "c3-3-c"),

            "c5-1-a" => ("implications", "broader_significance", "field_contribution", "c5-1-c"),
            "c5-1-b" => ("implications", "broader_significance", "significance_from_findings", "c5-1-c"),
            "c5-2-a" => ("implications", "practical_implications", "real_world_relevance", "c5-2-c"),
            "c5-2-b" => ("implications", "practical_implications", "policy_decision_impact", "c5-2-c"),
            "c5-3-a" => ("implications", "limits_of_interpretation", "main_constraints", "c5-3-c"),
            "c5-3-b" => ("implications", "limits_of_interpretation", "limits_from_method", "c5-3-c"),

            _ => (null!, null!, null!, null!)
        };

        if (focus is null)
        {
            return false;
        }

        drillNode = new TutorDrillNode(
            Focus: focus,
            ChildTarget: childTarget,
            DrillTarget: drillTarget,
            Query: "",
            Cites: new List<int>(),
            Summary: "",
            Depth: session.DrillPath?.Count ?? 0
        );

        return true;
    }

    private static bool IsAcademicRecursiveDrillChoice(string choiceId)
    {
        return choiceId.StartsWith("drill:", StringComparison.OrdinalIgnoreCase) ||
            choiceId is "c4-1" or "c4-2" or "c4-3" ||
            TryResolveAcademicDrillChoice(
                new TutorSession("", Guid.Empty, DocType.AcademicResearch, null, "", new List<string>(), new List<int>(), new List<string>(), null),
                choiceId,
                out _,
                out _);
    }

    private static bool IsAcademicFirstLevelChildChoice(string choiceId)
    {
        return choiceId is
            "c1-1" or "c1-2" or "c1-3" or
            "c2-1" or "c2-2" or "c2-3" or
            "c3-1" or "c3-2" or "c3-3" or
            "c5-1" or "c5-2" or "c5-3";
    }
}
