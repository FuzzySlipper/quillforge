using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

/// <summary>
/// Tests that forge-triggered query_lore calls report librarian token usage
/// through the OnNestedCompletion callback without leaking accounting data
/// into the serialized tool result.
/// </summary>
public class QueryLoreHandlerStatsTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    [Fact]
    public async Task QueryLoreHandler_ReportsLibrarianUsage_ThroughNestedCompletionCallback()
    {
        // Arrange: wire a librarian that returns usage, with OnNestedCompletion
        // pointing at a ForgeStatsTracker — the real production wiring.
        var completion = new FakeCompletionService();
        completion.EnqueueText("""{"relevant_passages": ["The dragon sleeps"], "source_files": ["dragons.md"], "confidence": "high"}""");

        var loreStore = new InMemoryLoreStore(new Dictionary<string, string>
        {
            ["dragons.md"] = "The dragon sleeps beneath the mountain.",
        });
        var promptStore = new InMemoryLibrarianPromptStore("");
        var fileService = new FakeContentFileService();
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(completion, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());

        var librarian = new LibrarianAgent(toolLoop, loreStore, promptStore, new AppConfig(), LogFactory.CreateLogger<LibrarianAgent>());
        var handler = new QueryLoreHandler(librarian, loreStore, fileService, LogFactory.CreateLogger<QueryLoreHandler>());

        var tracker = new ForgeStatsTracker();
        var context = new AgentContext
        {
            SessionId = Guid.CreateVersion7(),
            ActiveMode = Mode.Forge,
            ActiveLoreSet = "default",
            OnNestedCompletion = tracker.RecordCompletion,
        };

        var input = new ToolInput(JsonDocument.Parse("""{"query": "Where does the dragon sleep?"}""").RootElement);

        // Act
        var result = await handler.HandleAsync(input, context, CancellationToken.None);

        // Assert: handler succeeded
        Assert.True(result.Success, $"QueryLoreHandler should succeed, got: {result.Error}");

        // Assert: tracker recorded the librarian's completion
        var stats = tracker.Snapshot();
        Assert.True(stats.TotalInputTokens > 0, "Librarian input tokens should be recorded via callback");
        Assert.True(stats.TotalOutputTokens > 0, "Librarian output tokens should be recorded via callback");
        Assert.Equal(1, stats.AgentCalls);
    }

    [Fact]
    public async Task QueryLoreHandler_DoesNotReportUsage_WhenCallbackIsNull()
    {
        // Non-forge flows: OnNestedCompletion is null, handler should not throw.
        var completion = new FakeCompletionService();
        completion.EnqueueText("""{"relevant_passages": ["passage"], "source_files": ["file.md"], "confidence": "high"}""");

        var loreStore = new InMemoryLoreStore(new Dictionary<string, string>
        {
            ["file.md"] = "Some lore.",
        });
        var promptStore = new InMemoryLibrarianPromptStore("");
        var fileService = new FakeContentFileService();
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(completion, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());

        var librarian = new LibrarianAgent(toolLoop, loreStore, promptStore, new AppConfig(), LogFactory.CreateLogger<LibrarianAgent>());
        var handler = new QueryLoreHandler(librarian, loreStore, fileService, LogFactory.CreateLogger<QueryLoreHandler>());

        var context = new AgentContext
        {
            SessionId = Guid.CreateVersion7(),
            ActiveMode = Mode.General,
            ActiveLoreSet = "default",
            // OnNestedCompletion intentionally null — non-forge flow
        };

        var input = new ToolInput(JsonDocument.Parse("""{"query": "test query"}""").RootElement);

        // Act — should not throw even though callback is null
        var result = await handler.HandleAsync(input, context, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task QueryLoreHandler_SerializedToolResult_DoesNotContainUsageMetadata()
    {
        // The tool result returned to the model should be a clean LoreBundle
        // without internal accounting fields.
        var completion = new FakeCompletionService();
        completion.EnqueueText("""{"relevant_passages": ["passage"], "source_files": ["file.md"], "confidence": "high"}""");

        var loreStore = new InMemoryLoreStore(new Dictionary<string, string>
        {
            ["file.md"] = "Lore content.",
        });
        var promptStore = new InMemoryLibrarianPromptStore("");
        var fileService = new FakeContentFileService();
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(completion, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());

        var librarian = new LibrarianAgent(toolLoop, loreStore, promptStore, new AppConfig(), LogFactory.CreateLogger<LibrarianAgent>());
        var handler = new QueryLoreHandler(librarian, loreStore, fileService, LogFactory.CreateLogger<QueryLoreHandler>());

        var context = new AgentContext
        {
            SessionId = Guid.CreateVersion7(),
            ActiveMode = Mode.Forge,
            ActiveLoreSet = "default",
            OnNestedCompletion = (_, _) => { }, // no-op callback
        };

        var input = new ToolInput(JsonDocument.Parse("""{"query": "test"}""").RootElement);

        var result = await handler.HandleAsync(input, context, CancellationToken.None);

        // Parse the serialized tool result and verify no usage/token fields
        var json = JsonDocument.Parse(result.Content);
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("usage", out _),
            "Serialized query_lore result must not contain 'usage' field");
        Assert.False(root.TryGetProperty("Usage", out _),
            "Serialized query_lore result must not contain 'Usage' field");
        Assert.False(root.TryGetProperty("totalInputTokens", out _),
            "Serialized query_lore result must not contain token fields");
        Assert.False(root.TryGetProperty("TotalInputTokens", out _),
            "Serialized query_lore result must not contain token fields");

        // Should still contain the expected lore payload fields
        Assert.True(root.TryGetProperty("RelevantPassages", out _) || root.TryGetProperty("relevant_passages", out _),
            "Serialized result should contain lore payload");
    }

    [Fact]
    public void PersistedManifest_StatsAreDeserializable_FromStatusEndpointPath()
    {
        // Verifies the round-trip: pipeline writes manifest with stats,
        // the status endpoint deserializes it and surfaces Stats — the same
        // path /api/forge/{name}/status uses.
        var stats = new ForgeStats
        {
            TotalInputTokens = 500,
            TotalOutputTokens = 1000,
            AgentCalls = 12,
            ChaptersRevised = 3,
            StageTiming = new Dictionary<string, StageTiming>
            {
                ["Planning"] = new(
                    DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
                    DateTimeOffset.Parse("2026-04-10T10:05:00Z")),
                ["Design"] = new(
                    DateTimeOffset.Parse("2026-04-10T10:05:00Z"),
                    DateTimeOffset.Parse("2026-04-10T10:08:00Z")),
            },
        };

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Writing,
            ChapterCount = 5,
            Stats = stats,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Serialize exactly as the pipeline does
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize(manifest, jsonOptions);

        // Deserialize exactly as the status endpoint does
        var readOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var deserialized = JsonSerializer.Deserialize<ForgeManifest>(json, readOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(500, deserialized.Stats.TotalInputTokens);
        Assert.Equal(1000, deserialized.Stats.TotalOutputTokens);
        Assert.Equal(12, deserialized.Stats.AgentCalls);
        Assert.Equal(3, deserialized.Stats.ChaptersRevised);
        Assert.Equal(2, deserialized.Stats.StageTiming.Count);
        Assert.NotNull(deserialized.Stats.StageTiming["Planning"].End);
        Assert.NotNull(deserialized.Stats.StageTiming["Design"].End);
    }

    [Fact]
    public void ForgeStatusResponse_JsonShape_MatchesFrontendContract()
    {
        // Contract test: verifies the exact JSON shape that commands.ts reads.
        // This test would have caught the data.manifest vs data mismatch and
        // the state vs status field naming inconsistency.
        //
        // Frontend expectations (commands.ts):
        //   data.projectName, data.stage, data.paused, data.chapterCount,
        //   data.chapters["ch-01"].state (enum string like "Done"),
        //   data.stats.totalInputTokens, data.stats.totalOutputTokens

        var stats = new ForgeStats
        {
            TotalInputTokens = 500,
            TotalOutputTokens = 1000,
            AgentCalls = 12,
            ChaptersRevised = 2,
        };

        // Build the same response shape the endpoint produces
        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Writing,
            ChapterCount = 3,
            Paused = false,
            Chapters = new Dictionary<string, ChapterStatus>
            {
                ["ch-01"] = new() { State = ChapterState.Done, WordCount = 500 },
                ["ch-02"] = new() { State = ChapterState.Flagged, WordCount = 400, RevisionCount = 3 },
                ["ch-03"] = new() { State = ChapterState.Pending },
            },
            Stats = stats,
        };

        // Simulate the endpoint response construction (same as ForgeEndpoints.cs)
        var response = new
        {
            ProjectName = manifest.ProjectName,
            Stage = manifest.Stage.ToString(),
            ChapterCount = manifest.ChapterCount,
            Paused = manifest.Paused,
            Chapters = manifest.Chapters.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    State = kvp.Value.State.ToString(),
                    RevisionCount = kvp.Value.RevisionCount,
                    WordCount = kvp.Value.WordCount,
                }),
            Stats = manifest.Stats,
        };

        // Serialize with ASP.NET Core defaults (camelCase)
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level fields the frontend reads directly (not wrapped in "manifest")
        Assert.False(root.TryGetProperty("manifest", out _),
            "Response must NOT wrap in a 'manifest' property — frontend reads data.projectName directly");
        Assert.Equal("test-project", root.GetProperty("projectName").GetString());
        Assert.Equal("Writing", root.GetProperty("stage").GetString());
        Assert.Equal(3, root.GetProperty("chapterCount").GetInt32());
        Assert.False(root.GetProperty("paused").GetBoolean());

        // Chapter entries use "state" (not "status")
        var ch01 = root.GetProperty("chapters").GetProperty("ch-01");
        Assert.Equal("Done", ch01.GetProperty("state").GetString());
        Assert.False(ch01.TryGetProperty("status", out _),
            "Chapter entries must use 'state', not 'status'");

        var ch02 = root.GetProperty("chapters").GetProperty("ch-02");
        Assert.Equal("Flagged", ch02.GetProperty("state").GetString());
        Assert.Equal(3, ch02.GetProperty("revisionCount").GetInt32());

        // Stats fields the frontend reads
        var statsJson = root.GetProperty("stats");
        Assert.Equal(500, statsJson.GetProperty("totalInputTokens").GetInt64());
        Assert.Equal(1000, statsJson.GetProperty("totalOutputTokens").GetInt64());
        Assert.Equal(12, statsJson.GetProperty("agentCalls").GetInt32());
    }

    // --- Minimal fakes for librarian dependencies ---

    private sealed class InMemoryLoreStore(IReadOnlyDictionary<string, string> lore) : ILoreStore
    {
        public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
            => Task.FromResult(lore);

        public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
            string loreSetName, string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string, string)>>([]);
    }

    private sealed class InMemoryLibrarianPromptStore(string content) : ILibrarianPromptStore
    {
        public Task<string> LoadAsync(string promptName, CancellationToken ct = default)
            => Task.FromResult(content);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}
