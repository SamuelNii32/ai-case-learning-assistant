using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TutorAction
{
    ExploreFocus,
    ExploreChildTopic,
    DrillDeeper,
    Recap,
    ChangeFocus
}

public record TutorChoice(
    string Id,
    string Label,
    TutorAction Action,
    string? Target = null
);

public record TutorStartRequest(
    string? Focus = null
);

public record TutorStepRequest(
    string SessionId,
    string ChoiceId
);

public record TutorAnswerRequest(
    string SessionId,
    string StepId,
    string Answer
);

public record TutorFeedback(
    double Score,
    string Verdict,
    string Hint
);

public record TutorResponse(
    string SessionId,
    string Narrative,
    List<TutorChoice> Choices,
    List<int> Cites,
    string StepSummary,
    string Stage = "step",
    string? StepId = null,
    string? Question = null,
    int? StepNumber = null,
    int? TotalSteps = null,
    TutorFeedback? Feedback = null
);

public record TutorSession(
    string SessionId,
    Guid UploadId,
    DocType Category,
    string? Focus,
    string CurrentNode,
    List<string> VisitedTopics,
    List<int> VisitedPages,
    List<string> History,
    string? LastStepSummary,
    List<TutorDrillNode>? DrillPath = null,
    List<TutorDrillNode>? PendingDrillChoices = null
);

public record TutorDrillNode(
    string Focus,
    string ChildTarget,
    string DrillTarget,
    string Query,
    List<int> Cites,
    string Summary,
    int Depth
);

public static class TutorSessionStore
{
    public static readonly ConcurrentDictionary<string, TutorSession> Sessions = new();
}
