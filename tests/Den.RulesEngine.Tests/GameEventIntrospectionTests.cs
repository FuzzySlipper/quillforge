namespace Den.RulesEngine.Tests;

public sealed class GameEventIntrospectionTests
{
    [Fact]
    public void Inspect_ExtractsCoreEventFieldsForReplayInspectorAndTraceConsumers()
    {
        var gameId = new GameInstanceId("game-introspection");
        var pendingInputId = new PendingInputId("pending-1");
        var participantId = new ParticipantId("agent-1");
        var commandId = GameIntentCommandId.NewId();
        var rejectedCommand = new SubmitPlayerChoiceIntentCommand(
            commandId,
            gameId,
            pendingInputId,
            participantId,
            "alpha");

        var cases = new GameEventIntrospectionCase[]
        {
            new GameEventIntrospectionCase(
                PlayerChoiceSubmittedEvent.Create(
                    gameId,
                    pendingInputId,
                    participantId,
                    "alpha",
                    GameEventVisibility.Public),
                "agent-1",
                "pending-1",
                "alpha",
                null,
                null),
            new GameEventIntrospectionCase(
                PendingInputRequestedEvent.Create(
                    gameId,
                    pendingInputId,
                    participantId,
                    new GameStageId("vote"),
                    "vote"),
                "agent-1",
                "pending-1",
                null,
                null,
                null),
            new GameEventIntrospectionCase(
                AgentResponseRejectedEvent.Create(
                    gameId,
                    pendingInputId,
                    participantId,
                    "parse-fail",
                    "The response was not JSON.",
                    GameEventVisibility.HiddenSystemOnly),
                "agent-1",
                "pending-1",
                null,
                "parse-fail",
                null),
            new GameEventIntrospectionCase(
                NoActionTakenEvent.Create(
                    gameId,
                    pendingInputId,
                    participantId,
                    "timeout"),
                "agent-1",
                "pending-1",
                null,
                "timeout",
                null),
            new GameEventIntrospectionCase(
                IntentCommandRejectedEvent.Create(
                    rejectedCommand,
                    "illegal_choice",
                    "Choice is not legal for the pending input."),
                null,
                null,
                null,
                "illegal_choice",
                null),
            new GameEventIntrospectionCase(
                RoundEndedEvent.Create(gameId, 1, "harness-round-boundary"),
                null,
                null,
                null,
                "harness-round-boundary",
                null),
            new GameEventIntrospectionCase(
                GameAbortedEvent.Create(gameId, "harness-abort-edge-case"),
                null,
                null,
                null,
                "harness-abort-edge-case",
                null),
            new GameEventIntrospectionCase(
                GameEndedEvent.Create(gameId, "villagers_win"),
                null,
                null,
                null,
                null,
                "villagers_win"),
        };

        foreach (var item in cases)
        {
            var facts = GameEventIntrospection.Inspect(item.Event);

            Assert.Equal(item.ParticipantId, facts.ParticipantId);
            Assert.Equal(item.PendingInputId, facts.PendingInputId);
            Assert.Equal(item.ChoiceName, facts.ChoiceName);
            Assert.Equal(item.ReasonCode, facts.ReasonCode);
            Assert.Equal(item.OutcomeName, facts.OutcomeName);
        }
    }

    [Fact]
    public void ReplaySignature_UsesSharedIntrospectionFacts()
    {
        var gameId = new GameInstanceId("game-introspection");
        var gameEvent = PlayerChoiceSubmittedEvent.Create(
            gameId,
            new PendingInputId("pending-1"),
            new ParticipantId("agent-1"),
            "alpha",
            GameEventVisibility.Public)
            .WithJournalMetadata(GameEventId.NewId(), 7, DateTimeOffset.Parse("2026-04-29T12:00:00+00:00"));

        var facts = GameEventIntrospection.Inspect(gameEvent);
        var signature = GameEventReplaySignature.FromEvent(gameEvent);

        Assert.Equal(facts.ParticipantId, signature.ParticipantId);
        Assert.Equal(facts.PendingInputId, signature.PendingInputId);
        Assert.Equal(facts.ChoiceName, signature.ChoiceName);
        Assert.Equal(facts.ReasonCode, signature.ReasonCode);
        Assert.Equal(facts.OutcomeName, signature.OutcomeName);
    }

    private sealed record GameEventIntrospectionCase(
        IGameEvent Event,
        string? ParticipantId,
        string? PendingInputId,
        string? ChoiceName,
        string? ReasonCode,
        string? OutcomeName);
}
