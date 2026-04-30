using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Architecture.Tests;

public sealed class GameDiagnosticLogServiceTests
{
    [Fact]
    public async Task GetLogAsync_CollectsChronologicalGameDiagnosticsIncludingRejectionAndPromptData()
    {
        var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var runtime = new GameRuntimeState
        {
            Status = GameRuntimeStatus.WaitingForInput,
            GameInstanceId = "game-test",
            TemplateId = "village",
            ModuleId = "werewolf",
            ModuleVersion = "0.1.0",
            StartedAt = Instant(0),
            LastUpdatedAt = Instant(4),
            ParticipantBindings =
            [
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "human-1",
                    DisplayName = "Human",
                    Kind = GameRuntimeParticipantKind.Human,
                },
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "agent-1",
                    DisplayName = "Mira",
                    Kind = GameRuntimeParticipantKind.Agent,
                    ProviderAlias = "local",
                    ModelOverride = "llama3.2",
                },
            ],
            HostRecords =
            [
                new GameRuntimeHostRecord
                {
                    Sequence = 1,
                    Kind = GameRuntimeHostRecordKind.Started,
                    OccurredAt = Instant(0),
                    ReasonCode = "game_started",
                    Summary = "Game runtime started with 2 engine event(s).",
                },
                new GameRuntimeHostRecord
                {
                    Sequence = 2,
                    Kind = GameRuntimeHostRecordKind.CommunicationRejected,
                    OccurredAt = Instant(3),
                    ReasonCode = "public_messages_disabled",
                    Summary = "Communication operation 'post_game_public_message' rejected: public messages are disabled.",
                },
            ],
            Communication = new ParticipantCommunicationState
            {
                ChannelMessages =
                [
                    new ParticipantChannelMessage(
                        Guid.Parse("11111111-2222-3333-4444-555555555555"),
                        5,
                        new ParticipantMessageAuthor(new GameParticipantId("human-1"), ParticipantMessageAuthorKind.Human),
                        "hello table",
                        Instant(2)),
                ],
            },
            PromptEnvelopes =
            [
                new GameRuntimeAgentPromptEnvelope
                {
                    EnvelopeId = "game-agent-1",
                    ParticipantId = "agent-1",
                    CreatedAt = Instant(2),
                    EngineCursorSequence = 7,
                    CommunicationCursorSequence = 5,
                    MemoryRevision = 1,
                    ProviderAlias = "local",
                    Model = "llama3.2",
                    PromptTokens = 123,
                    ResponseTokens = 45,
                    PromptContentHash = "prompt-hash",
                    ResponseContentHash = "response-hash",
                    PromptText = "private prompt body",
                    ResponseText = "{\"accepted\":false,\"reasonCode\":\"parse-fail\"}",
                },
            ],
        };
        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        tracker.Record(sessionId, "game-agent:agent-1", new TokenUsage(123, 45));
        var service = new GameDiagnosticLogService(new FakeGameRuntimeService(runtime), tracker);

        var log = await service.GetLogAsync(sessionId);

        Assert.True(log.HasGame);
        Assert.Equal("game-test", log.GameInstanceId);
        Assert.Null(log.RequestedGameInstanceId);
        Assert.True(log.ScopeMatchesActiveGame);
        Assert.Contains("Provider API keys", log.PrivacyNotice, StringComparison.Ordinal);
        Assert.Null(log.Limit);
        Assert.Null(log.BeforeSequence);
        Assert.Empty(log.Categories);
        Assert.Equal(log.Events.Count, log.TotalEventCount);
        Assert.Equal(log.Events.Count, log.FilteredEventCount);
        Assert.Equal(log.Events.Count, log.ReturnedEventCount);
        Assert.False(log.HasMore);
        Assert.Null(log.NextBeforeSequence);
        Assert.Equal(log.Events.Select(item => item.Timestamp).OrderBy(item => item).ToArray(), log.Events.Select(item => item.Timestamp).ToArray());
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Communication && item.Operation == "public_message_posted");
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.LlmProvider && item.PromptPreview!.Contains("private prompt", StringComparison.Ordinal));
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Rejection && item.ReasonCode == "public_messages_disabled");
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.TokenUsage && item.Summary.Contains("123 input tokens", StringComparison.Ordinal));
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Persistence && item.Operation == "session_state_persisted");
    }

    [Fact]
    public async Task GetLogAsync_AppliesCategoryLimitAndBeforeSequenceWithoutRenumberingEvents()
    {
        var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var runtime = new GameRuntimeState
        {
            Status = GameRuntimeStatus.Running,
            GameInstanceId = "game-test",
            TemplateId = "village",
            ModuleId = "werewolf",
            ModuleVersion = "0.1.0",
            StartedAt = Instant(0),
            LastUpdatedAt = Instant(5),
            HostRecords =
            [
                RejectionRecord(1, "first_rejection", Instant(1)),
                RejectionRecord(2, "second_rejection", Instant(2)),
                RejectionRecord(3, "third_rejection", Instant(3)),
            ],
        };
        var service = new GameDiagnosticLogService(
            new FakeGameRuntimeService(runtime),
            new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance));

        var latest = await service.GetLogAsync(sessionId, new GameDiagnosticLogQuery
        {
            Limit = 1,
            Categories = [GameDiagnosticLogCategory.Rejection],
        });

        Assert.Equal(1, latest.Limit);
        Assert.Equal([GameDiagnosticLogCategory.Rejection], latest.Categories);
        Assert.Single(latest.Events);
        Assert.Equal(3, latest.FilteredEventCount);
        Assert.Equal(1, latest.ReturnedEventCount);
        Assert.True(latest.HasMore);
        Assert.Equal(GameDiagnosticLogCategory.Rejection, latest.Events.Single().Category);
        Assert.Equal("third_rejection", latest.Events.Single().ReasonCode);
        Assert.Equal(latest.Events.Single().Sequence, latest.NextBeforeSequence);

        var older = await service.GetLogAsync(sessionId, new GameDiagnosticLogQuery
        {
            Limit = 1,
            BeforeSequence = latest.NextBeforeSequence,
            Categories = [GameDiagnosticLogCategory.Rejection],
        });

        Assert.Equal(latest.NextBeforeSequence, older.BeforeSequence);
        Assert.Single(older.Events);
        Assert.Equal("second_rejection", older.Events.Single().ReasonCode);
        Assert.True(older.Events.Single().Sequence < latest.Events.Single().Sequence);
    }

    [Fact]
    public async Task GetLogAsync_WhenRequestedGameScopeDoesNotMatch_DoesNotIncludeCurrentRuntimeEvents()
    {
        var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var runtime = new GameRuntimeState
        {
            Status = GameRuntimeStatus.Running,
            GameInstanceId = "game-old",
            TemplateId = "village",
            ModuleId = "werewolf",
            ModuleVersion = "0.1.0",
            StartedAt = Instant(0),
            LastUpdatedAt = Instant(1),
            HostRecords =
            [
                new GameRuntimeHostRecord
                {
                    Sequence = 1,
                    Kind = GameRuntimeHostRecordKind.CommunicationRejected,
                    OccurredAt = Instant(1),
                    ReasonCode = "public_channel_forbidden",
                    Summary = "Old game rejection should not leak into requested scope.",
                },
            ],
        };
        var service = new GameDiagnosticLogService(
            new FakeGameRuntimeService(runtime),
            new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance));

        var log = await service.GetLogAsync(sessionId, new GameDiagnosticLogQuery { RequestedGameInstanceId = "game-new" });

        Assert.False(log.HasGame);
        Assert.Null(log.GameInstanceId);
        Assert.Equal("game-new", log.RequestedGameInstanceId);
        Assert.False(log.ScopeMatchesActiveGame);
        Assert.Contains(log.Events, item => item.Operation == "diagnostic_scope_mismatch");
        Assert.DoesNotContain(log.Events, item => item.ReasonCode == "public_channel_forbidden");
        Assert.DoesNotContain(log.Events, item => item.Operation == "runtime_snapshot");
    }

    private static GameRuntimeHostRecord RejectionRecord(int sequence, string reasonCode, DateTimeOffset occurredAt) =>
        new()
        {
            Sequence = sequence,
            Kind = GameRuntimeHostRecordKind.CommunicationRejected,
            OccurredAt = occurredAt,
            ReasonCode = reasonCode,
            Summary = $"Communication rejected: {reasonCode}.",
        };

    private static DateTimeOffset Instant(int seconds) =>
        DateTimeOffset.Parse("2026-04-29T12:00:00+00:00").AddSeconds(seconds);

    private sealed class FakeGameRuntimeService : IGameRuntimeService
    {
        private readonly GameRuntimeState _runtime;

        public FakeGameRuntimeService(GameRuntimeState runtime)
        {
            _runtime = runtime;
        }

        public Task<GameRuntimeState?> LoadViewAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult<GameRuntimeState?>(_runtime);

        public Task<SessionMutationResult<GameRuntimeMutationResult>> StartAsync(Guid sessionId, StartGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMutationResult>> ApplyEngineCommandAsync(Guid sessionId, ApplyGameRuntimeEngineCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMutationResult>> ResumeAsync(Guid sessionId, ResumeGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsync(Guid sessionId, AbortGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMutationResult>> AppendHostRecordAsync(Guid sessionId, AppendGameRuntimeHostRecordCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> PostPublicMessageAsync(Guid sessionId, PostGameRuntimePublicMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> SendDirectMessageAsync(Guid sessionId, SendGameRuntimeDirectMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimePromptMutationResult>> RecordAgentPromptAsync(Guid sessionId, RecordGameRuntimeAgentPromptCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMemorySummaryMutationResult>> RecordAgentMemorySummaryAsync(Guid sessionId, RecordGameRuntimeAgentMemorySummaryCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
