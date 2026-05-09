using System;
using System.Collections.Generic;
using System.Linq;

public static class TutorRecap
{
    public static TutorResponse FinalizeAcademicStep(
        TutorSession session,
        TutorResponse response,
        bool allowRecap)
    {
        var visited = session.VisitedPages ?? new List<int>();
        var newPages = response.Cites
            .Where(p => !visited.Contains(p))
            .Distinct()
            .ToList();

        var overlapCount = response.Cites
            .Count(p => visited.Contains(p));

        if (
            allowRecap &&
            (session.History?.Count ?? 0) >= 3 &&
            visited.Count > 0 &&
            newPages.Count == 0 &&
            overlapCount >= 2
        )
        {
            var recap = BuildAcademicRecapResponse(session);

            var recapSession = session with
            {
                History = AppendHistory(session.History, recap.StepSummary),
                LastStepSummary = recap.StepSummary
            };

            TutorSessionStore.Sessions[session.SessionId] = recapSession;
            return recap;
        }

        var updated = session with
        {
            VisitedPages = visited.Concat(response.Cites).Distinct().OrderBy(x => x).ToList(),
            History = AppendHistory(session.History, response.StepSummary),
            LastStepSummary = response.StepSummary
        };

        TutorSessionStore.Sessions[session.SessionId] = updated;
        return response;
    }

    private static List<string> AppendHistory(List<string> history, string stepSummary)
    {
        var next = history is null ? new List<string>() : new List<string>(history);
        if (!string.IsNullOrWhiteSpace(stepSummary))
        {
            next.Add(stepSummary);
        }
        return next;
    }

    private static TutorResponse BuildAcademicRecapResponse(TutorSession session)
    {
        var cites = (session.VisitedPages ?? new List<int>())
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (cites.Count == 0)
        {
            cites = new List<int> { 1 };
        }

        var first = cites[0];
        var mid = cites[cites.Count / 2];
        var last = cites[^1];

        var focusLabel = string.IsNullOrWhiteSpace(session.Focus) ? "current focus" : session.Focus;

        List<TutorChoice> choices;

        if (string.Equals(session.Focus, "methodology", StringComparison.OrdinalIgnoreCase))
        {
            choices = new List<TutorChoice>
            {
                new("recap-m1", "The method still has another angle.\nA different part of the design...", TutorAction.ChangeFocus, "methodology"),
                new("recap-m2", "The findings now sit on this method.\nWhat the design makes possible...", TutorAction.ChangeFocus, "findings"),
                new("recap-m3", "The method connects back to the frame.\nThe conceptual logic underneath...", TutorAction.ChangeFocus, "theory"),
                new("recap-m4", "The key terms may now read differently.\nConcepts behind the method...", TutorAction.ChangeFocus, "concepts")
            };
        }
        else if (string.Equals(session.Focus, "findings", StringComparison.OrdinalIgnoreCase))
        {
            choices = new List<TutorChoice>
            {
                new("recap-f1", "The findings still have a contrast.\nAnother result may complicate the picture...", TutorAction.ChangeFocus, "findings"),
                new("recap-f2", "The design sits beneath these findings.\nHow support was produced...", TutorAction.ChangeFocus, "methodology"),
                new("recap-f3", "The results point back to theory.\nThe broader debate underneath...", TutorAction.ChangeFocus, "theory"),
                new("recap-f4", "The findings depend on key terms.\nConcepts shaping interpretation...", TutorAction.ChangeFocus, "concepts")
            };
        }
        else
        {
            choices = new List<TutorChoice>
            {
                new("recap-a1", "This focus still has an open thread.\nOne more angle here...", TutorAction.ChangeFocus, session.Focus ?? "overview"),
                new("recap-a2", "The findings may reframe this path.\nThe study’s main conclusions...", TutorAction.ChangeFocus, "findings"),
                new("recap-a3", "The method may explain the pattern.\nHow the study was designed...", TutorAction.ChangeFocus, "methodology"),
                new("recap-a4", "The key concepts may hold the link.\nTerms shaping the argument...", TutorAction.ChangeFocus, "concepts")
            };
        }

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative:
                $"This path has reached a natural recap point in the {focusLabel} focus. [p:{first}]\n\n" +
                $"The recent steps have reinforced the same core pages, which suggests that the main idea in this branch has now been established. [p:{mid}]\n\n" +
                $"From here, the next step can either deepen the same focus from another angle or move to a different major part of the paper. [p:{last}]",
            Choices: choices,
            Cites: cites.Count <= 3 ? cites : new List<int> { first, mid, last },
            StepSummary: $"Recap: {focusLabel}"
        );
    }
}
