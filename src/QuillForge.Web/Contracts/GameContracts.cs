using QuillForge.Core.Models;

namespace QuillForge.Web.Contracts;

public sealed record StartGameRequest
{
    public required string TemplateId { get; init; }

    public string? UserDisplayName { get; init; }

    public long? Seed { get; init; }
}

public sealed record SubmitGameActionRequest
{
    public required string ParticipantId { get; init; }

    public string? PendingInputId { get; init; }

    public string? ChoiceName { get; init; }

    public string? Text { get; init; }
}

public sealed record PostGamePublicMessageRequest
{
    public string? ParticipantId { get; init; }

    public string? Text { get; init; }

    public ParticipantMessageAuthorKind AuthorKind { get; init; } = ParticipantMessageAuthorKind.Human;
}

public sealed record SendGameDirectMessageRequest
{
    public string? ParticipantId { get; init; }

    public IReadOnlyList<string> RecipientParticipantIds { get; init; } = [];

    public string? Text { get; init; }

    public ParticipantMessageAuthorKind AuthorKind { get; init; } = ParticipantMessageAuthorKind.Human;
}

public sealed record EndGameRequest
{
    public string OutcomeName { get; init; } = "ended_by_host";
}

public sealed record AbortGameRequest
{
    public string ReasonCode { get; init; } = "aborted_by_host";
}

public sealed record GameViewResponse
{
    public required GameBridgeView View { get; init; }
}

public sealed record GameInspectorResponse
{
    public required GameInspectorProjection Inspector { get; init; }
}

public sealed record GameDiagnosticLogResponse
{
    public required GameDiagnosticLogProjection Log { get; init; }
}

public sealed record GameMutationResponse
{
    public required GameBridgeView View { get; init; }

    public required IReadOnlyList<string> RuntimeEventTypes { get; init; }

    public required IReadOnlyList<string> EngineEventTypes { get; init; }

    public required IReadOnlyList<string> CommunicationEventTypes { get; init; }
}

public sealed record GameMutationErrorResponse
{
    public required string Error { get; init; }

    public required string Message { get; init; }

    public string? ReasonCode { get; init; }

    public string? Operation { get; init; }

    public required string DiagnosticHint { get; init; }
}
