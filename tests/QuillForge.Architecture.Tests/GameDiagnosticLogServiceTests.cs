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
        Assert.Contains("Provider API keys", log.PrivacyNotice, StringComparison.Ordinal);
        Assert.Equal(log.Events.Select(item => item.Timestamp).OrderBy(item => item).ToArray(), log.Events.Select(item => item.Timestamp).ToArray());
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Communication && item.Operation == "public_message_posted");
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.LlmProvider && item.PromptPreview!.Contains("private prompt", StringComparison.Ordinal));
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Rejection && item.ReasonCode == "public_messages_disabled");
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.TokenUsage && item.Summary.Contains("123 input tokens", StringComparison.Ordinal));
        Assert.Contains(log.Events, item => item.Category == GameDiagnosticLogCategory.Persistence && item.Operation == "session_state_persisted");
    }

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

        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> PostPublicMessageAsync(Guid sessionId, PostGameRuntimePublicMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> SendDirectMessageAsync(Guid sessionId, SendGameRuntimeDirectMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimePromptMutationResult>> RecordAgentPromptAsync(Guid sessionId, RecordGameRuntimeAgentPromptCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMemorySummaryMutationResult>> RecordAgentMemorySummaryAsync(Guid sessionId, RecordGameRuntimeAgentMemorySummaryCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
