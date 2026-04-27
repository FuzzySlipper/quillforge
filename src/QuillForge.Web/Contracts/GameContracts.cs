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
    public required string ParticipantId { get; init; }

    public string Text { get; init; } = string.Empty;

    public ParticipantMessageAuthorKind AuthorKind { get; init; } = ParticipantMessageAuthorKind.Human;
}

public sealed record SendGameDirectMessageRequest
{
    public required string ParticipantId { get; init; }

    public IReadOnlyList<string> RecipientParticipantIds { get; init; } = [];

    public string Text { get; init; } = string.Empty;

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

public sealed record GameMutationResponse
{
    public required GameBridgeView View { get; init; }

    public required IReadOnlyList<string> RuntimeEventTypes { get; init; }

    public required IReadOnlyList<string> EngineEventTypes { get; init; }

    public required IReadOnlyList<string> CommunicationEventTypes { get; init; }
}
