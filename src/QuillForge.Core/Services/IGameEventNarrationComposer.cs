using Den.RulesEngine;

namespace QuillForge.Core.Services;

public interface IGameEventNarrationComposer
{
    string ComposeSummary(IGameEvent gameEvent);
}

public interface IGameModuleEventNarrationComposer
{
    bool CanCompose(IGameEvent gameEvent);

    string ComposeSummary(IGameEvent gameEvent);
}

public sealed class CompositeGameEventNarrationComposer : IGameEventNarrationComposer
{
    private readonly IReadOnlyList<IGameModuleEventNarrationComposer> _moduleComposers;
    private readonly DefaultGameEventNarrationComposer _fallback;

    public CompositeGameEventNarrationComposer(
        IEnumerable<IGameModuleEventNarrationComposer> moduleComposers,
        DefaultGameEventNarrationComposer fallback)
    {
        ArgumentNullException.ThrowIfNull(moduleComposers);
        ArgumentNullException.ThrowIfNull(fallback);

        _moduleComposers = moduleComposers.ToArray();
        _fallback = fallback;
    }

    public string ComposeSummary(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        foreach (var composer in _moduleComposers)
        {
            if (composer.CanCompose(gameEvent))
            {
                return composer.ComposeSummary(gameEvent);
            }
        }

        return _fallback.ComposeSummary(gameEvent);
    }
}

public sealed class DefaultGameEventNarrationComposer : IGameEventNarrationComposer
{
    public string ComposeSummary(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            GameStartedEvent started => $"Game started with module {started.ModuleId.Value}.",
            PlayerChoiceSubmittedEvent submitted => $"{submitted.ParticipantId.Value} submitted a choice.",
            AgentResponseRejectedEvent rejected => $"Agent response from {rejected.ParticipantId.Value} was rejected: {rejected.ReasonCode}.",
            NoActionTakenEvent noAction => $"{noAction.ParticipantId.Value} took no action: {noAction.ReasonCode}.",
            PendingInputRequestedEvent pending => $"{pending.ParticipantId.Value} was asked for {pending.IntentName}.",
            StageAdvancedEvent advanced => $"Stage advanced from {advanced.PreviousStageId.Value} to {advanced.NextStageId.Value}.",
            RoundEndedEvent ended => $"Round {ended.RoundNumber} ended: {ended.ReasonCode}.",
            RoundStartedEvent startedRound => $"Round {startedRound.RoundNumber} started.",
            GameEndedEvent ended => $"Game ended: {ended.OutcomeName}.",
            GameAbortedEvent aborted => $"Game aborted: {aborted.ReasonCode}.",
            _ => $"{gameEvent.GetType().Name} occurred.",
        };
    }
}
