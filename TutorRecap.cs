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

    private static List<string> AppendHistory(List<string>? history, string stepSummary)
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
                new("c2", "Stay with methodology\nExplore another part of the study design.", TutorAction.ChangeFocus, "methodology"),
                new("c1", "Move to findings\nSee what the method makes possible.", TutorAction.ChangeFocus, "findings"),
                new("c3", "Connect back to background\nReturn to the framing and prior-work problem.", TutorAction.ChangeFocus, "background"),
                new("c4", "Clarify key concepts\nReview the terms behind the method.", TutorAction.ChangeFocus, "concepts")
            };
        }
        else if (string.Equals(session.Focus, "findings", StringComparison.OrdinalIgnoreCase))
        {
            choices = new List<TutorChoice>
            {
                new("c1", "Stay with findings\nExplore another result or contrast.", TutorAction.ChangeFocus, "findings"),
                new("c2", "Move to methodology\nSee how the evidence was produced.", TutorAction.ChangeFocus, "methodology"),
                new("c3", "Return to the background\nSee the prior-work problem behind these results.", TutorAction.ChangeFocus, "background"),
                new("c4", "Clarify key concepts\nReview the terms shaping interpretation.", TutorAction.ChangeFocus, "concepts")
            };
        }
        else
        {
            choices = new List<TutorChoice>
            {
                new(FocusChoiceId(session.Focus), "Stay with this focus\nExplore another supported angle here.", TutorAction.ChangeFocus, session.Focus ?? "overview"),
                new("c1", "Move to findings\nRead the study's main conclusions.", TutorAction.ChangeFocus, "findings"),
                new("c2", "Move to methodology\nSee how the study was designed.", TutorAction.ChangeFocus, "methodology"),
                new("c4", "Clarify key concepts\nReview the terms shaping the argument.", TutorAction.ChangeFocus, "concepts")
            };
        }

        var covered = BuildCoveredSummary(session.History);

        return new TutorResponse(
            SessionId: session.SessionId,
            Narrative:
                $"Quick recap: in the {focusLabel} focus, you worked through {covered}. [p:{first}]\n\n" +
                $"The important pattern is that the later steps kept returning to the same evidence, so this branch is no longer opening a clearly new supported idea. [p:{mid}]\n\n" +
                $"From here, the useful move is to carry that takeaway into another angle of the same focus, or switch to a different major part of the paper. [p:{last}]",
            Choices: choices,
            Cites: cites.Count <= 3 ? cites : new List<int> { first, mid, last },
            StepSummary: $"Recap: {focusLabel}",
            Stage: "recap"
        );
    }

    private static string BuildCoveredSummary(List<string>? history)
    {
        var items = (history ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(CleanSummaryLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(4)
            .ToList();

        if (items.Count == 0)
        {
            return "the main claim, the supporting evidence, and the next decision point";
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        if (items.Count == 2)
        {
            return $"{items[0]} and {items[1]}";
        }

        return string.Join(", ", items.Take(items.Count - 1)) + $", and {items[^1]}";
    }

    private static string CleanSummaryLabel(string summary)
    {
        var value = summary.Trim();

        if (value.StartsWith("Entered ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Entered ".Length..];
        }

        if (value.StartsWith("Returned to ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Returned to ".Length..];
        }

        if (value.StartsWith("Drill:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Drill:".Length..].Trim();
        }

        value = value
            .Replace(":", " - ")
            .Replace("_", " ")
            .Replace(" focus", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return value.ToLowerInvariant();
    }

    private static string FocusChoiceId(string? focus) => focus?.ToLowerInvariant() switch
    {
        "findings" => "c1",
        "methodology" => "c2",
        "background" => "c3",
        "concepts" => "c4",
        "implications" => "c5",
        _ => "back-academic"
    };
}
