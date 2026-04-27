namespace Den.RulesEngine;

public enum RulesGameStatus
{
    NotStarted,
    Running,
    WaitingForInput,
    Resolving,
    Ended,
    Aborted
}

public enum ParticipantKind
{
    Human,
    Agent,
    System
}

public enum PendingInputStatus
{
    Waiting,
    Submitted,
    TimedOut,
    Cancelled
}

public sealed record RulesGameState(
    GameInstanceId GameInstanceId,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    RulesGameStatus Status,
    DeterministicRandomState Random,
    GameRoundState Round,
    GameStageState Stage,
    IReadOnlyList<ParticipantState> Participants,
    IReadOnlyList<PendingInputState> PendingInputs,
    GameEventJournal EventJournal)
{
    public static RulesGameState CreateNotStarted(
        GameInstanceId gameInstanceId,
        GameModuleDescriptor module,
        long seed,
        IReadOnlyList<ParticipantState> participants)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(participants);

        return new RulesGameState(
            gameInstanceId,
            module.ModuleId,
            module.ModuleVersion,
            RulesGameStatus.NotStarted,
            DeterministicRandomState.Create(seed),
            GameRoundState.Initial,
            GameStageState.NotStarted,
            participants.ToArray(),
            [],
            GameEventJournal.Empty(gameInstanceId));
    }

    public ParticipantState? FindParticipant(ParticipantId participantId) =>
        Participants.FirstOrDefault(participant => participant.ParticipantId == participantId);

    public PendingInputState? FindPendingInput(PendingInputId pendingInputId) =>
        PendingInputs.FirstOrDefault(input => input.PendingInputId == pendingInputId);

    public RulesGameState WithPendingInputs(IReadOnlyList<PendingInputState> pendingInputs) =>
        this with { PendingInputs = pendingInputs.ToArray() };

    public RulesGameState WithEventJournal(GameEventJournal eventJournal) =>
        this with { EventJournal = eventJournal };
}

public sealed record RulesGameStateSnapshot(
    GameInstanceId GameInstanceId,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    RulesGameStatus Status,
    DeterministicRandomState Random,
    GameRoundState Round,
    GameStageState Stage,
    IReadOnlyList<ParticipantState> Participants,
    IReadOnlyList<PendingInputState> PendingInputs,
    GameEventJournalSnapshot EventJournal)
{
    public static RulesGameStateSnapshot FromState(RulesGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new RulesGameStateSnapshot(
            state.GameInstanceId,
            state.ModuleId,
            state.ModuleVersion,
            state.Status,
            state.Random,
            state.Round,
            state.Stage,
            state.Participants.ToArray(),
            state.PendingInputs.ToArray(),
            state.EventJournal.ToSnapshot());
    }

    public RulesGameState ToState() => new(
        GameInstanceId,
        ModuleId,
        ModuleVersion,
        Status,
        Random,
        Round,
        Stage,
        Participants.ToArray(),
        PendingInputs.ToArray(),
        EventJournal.ToJournal());
}

public sealed record ParticipantState(
    ParticipantId ParticipantId,
    string DisplayName,
    ParticipantKind Kind,
    IReadOnlyList<ParticipantSetId> ParticipantSetIds,
    bool IsActive = true)
{
    public static ParticipantState Human(ParticipantId participantId, string displayName) =>
        new(participantId, displayName, ParticipantKind.Human, []);

    public static ParticipantState Agent(ParticipantId participantId, string displayName) =>
        new(participantId, displayName, ParticipantKind.Agent, []);
}

public sealed record GameRoundState(int RoundNumber)
{
    public static GameRoundState Initial { get; } = new(0);
}

public sealed record GameStageState(
    GameStageId StageId,
    string DisplayName,
    int Sequence,
    bool AllowsPublicMessages,
    bool AllowsDirectMessages)
{
    public static GameStageState NotStarted { get; } = new(new GameStageId("not-started"), "Not started", 0, false, false);
}

public sealed record PendingInputState(
    PendingInputId PendingInputId,
    ParticipantId ParticipantId,
    GameStageId StageId,
    string IntentName,
    PendingInputStatus Status,
    IReadOnlyList<LegalIntentOption> LegalOptions)
{
    public bool IsWaitingFor(ParticipantId participantId) =>
        Status == PendingInputStatus.Waiting && ParticipantId == participantId;
}

public sealed record LegalIntentOption(
    string IntentName,
    string DisplayName,
    string Description);

public sealed record DeterministicRandomState(long Seed, int DrawCount)
{
    public static DeterministicRandomState Create(long seed) => new(seed, 0);

    public DeterministicRandomDraw NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound), "Upper bound must be positive.");
        }

        var raw = SplitMix64(unchecked((ulong)Seed + (ulong)DrawCount));
        var value = (int)(raw % (uint)exclusiveUpperBound);

        return new DeterministicRandomDraw(value, AfterDraws(1));
    }

    public DeterministicRandomState AfterDraws(int drawCount)
    {
        if (drawCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(drawCount), "Draw count cannot be negative.");
        }

        return this with { DrawCount = DrawCount + drawCount };
    }

    private static ulong SplitMix64(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15ul;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9ul;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBul;
            return value ^ (value >> 31);
        }
    }
}

public sealed record DeterministicRandomDraw(int Value, DeterministicRandomState State);
