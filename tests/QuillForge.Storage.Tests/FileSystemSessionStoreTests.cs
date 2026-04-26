using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public class FileSystemSessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemSessionStore _store;
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public FileSystemSessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-session-test-" + Guid.NewGuid().ToString("N")[..8]);
        var writer = new AtomicFileWriter(_loggerFactory.CreateLogger<AtomicFileWriter>());
        _store = new FileSystemSessionStore(_tempDir, writer,
            _loggerFactory.CreateLogger<FileSystemSessionStore>(), _loggerFactory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var tree = new ConversationTree(Guid.CreateVersion7(), "Test Session",
            _loggerFactory.CreateLogger<ConversationTree>());
        tree.Append(tree.RootId, "user", new MessageContent("Hello!"));
        tree.Append(
            tree.ActiveLeafId,
            "assistant",
            new MessageContent("Hi there!"),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
                StopReason = StopReason.EndTurn,
                Reasoning = "Keep the greeting brief.",
                ReasoningArtifacts =
                [
                    new ReasoningArtifact
                    {
                        AgentId = "assistant",
                        AgentLabel = "Assistant",
                        Content = "Keep the greeting brief.",
                        Sequence = 0,
                    },
                    new ReasoningArtifact
                    {
                        AgentId = "prose-writer",
                        AgentLabel = "Prose Writer",
                        Content = "Lead with the smile.",
                        Sequence = 1,
                    },
                ],
                ProviderReplay = new ReasoningReplayEnvelope(
                    "Hi there!",
                    "Keep the greeting brief.",
                    []),
            });

        await _store.SaveAsync(tree);
        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, $"{tree.SessionId}.json"));
        Assert.Contains("\"conversationMode\": \"roleplay\"", json);

        var loaded = await _store.LoadAsync(tree.SessionId);

        Assert.Equal(tree.SessionId, loaded.SessionId);
        Assert.Equal("Test Session", loaded.Name);
        Assert.Equal(tree.Count, loaded.Count);

        var thread = loaded.ToFlatThread();
        Assert.Equal(2, thread.Count);
        Assert.Equal("Hello!", thread[0].Content.GetText());
        Assert.Equal("Hi there!", thread[1].Content.GetText());
        Assert.Equal(Mode.Roleplay, thread[1].Metadata?.ConversationMode);
        Assert.Equal("Keep the greeting brief.", thread[1].Metadata?.Reasoning);
        var artifacts = thread[1].Metadata?.ReasoningArtifacts;
        Assert.NotNull(artifacts);
        Assert.Equal(2, artifacts.Count);
        Assert.Equal("assistant", artifacts[0].AgentId);
        Assert.Equal("Keep the greeting brief.", artifacts[0].Content);
        Assert.Equal("prose-writer", artifacts[1].AgentId);
        Assert.Equal("Lead with the smile.", artifacts[1].Content);
        var replay = Assert.IsType<ReasoningReplayEnvelope>(thread[1].Metadata?.ProviderReplay);
        Assert.Equal("Hi there!", replay.Content);
        Assert.Equal("Keep the greeting brief.", replay.ReasoningContent);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesBranches()
    {
        var tree = new ConversationTree(Guid.CreateVersion7(), "Branched",
            _loggerFactory.CreateLogger<ConversationTree>());
        var msg1 = tree.Append(tree.RootId, "user", new MessageContent("Hello"));
        tree.Append(msg1.Id, "assistant", new MessageContent("Branch A"));
        tree.Append(msg1.Id, "assistant", new MessageContent("Branch B"));

        await _store.SaveAsync(tree);
        var loaded = await _store.LoadAsync(tree.SessionId);

        // msg1 should have 2 children
        var node = loaded.GetNode(msg1.Id);
        Assert.NotNull(node);
        Assert.Equal(2, node.ChildIds.Count);
    }

    [Fact]
    public async Task SyncRoleplayTranscriptAsync_AfterPersistedSessionReload_UsesConversationModeMetadata()
    {
        var sessionId = Guid.CreateVersion7();
        var tree = new ConversationTree(sessionId, "Roleplay Session",
            _loggerFactory.CreateLogger<ConversationTree>());
        var user = tree.Append(tree.RootId, "user", new MessageContent("I knock twice."));
        tree.Append(
            user.Id,
            "assistant",
            new MessageContent("Nadia opens the peephole and lowers her voice."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await _store.SaveAsync(tree);

        var reloaded = await _store.LoadAsync(sessionId);
        var reloadedThread = reloaded.ToFlatThread();
        Assert.Equal(Mode.Roleplay, reloadedThread[1].Metadata?.ConversationMode);

        var writer = new AtomicFileWriter(_loggerFactory.CreateLogger<AtomicFileWriter>());
        var runtimeStore = new FileSystemSessionRuntimeStore(
            _tempDir,
            writer,
            _loggerFactory.CreateLogger<FileSystemSessionRuntimeStore>());
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "demo-campaign",
                CurrentFile = "scene-01.md",
                Character = "nadia",
            },
        });

        var storyStore = new FileSystemStoryStore(
            Path.Combine(_tempDir, ContentPaths.Story),
            writer,
            _loggerFactory.CreateLogger<FileSystemStoryStore>());
        var transcriptService = new SessionTranscriptService(
            _store,
            runtimeStore,
            new InMemorySessionMutationGate(_loggerFactory.CreateLogger<InMemorySessionMutationGate>()),
            storyStore,
            _loggerFactory.CreateLogger<SessionTranscriptService>());

        await transcriptService.SyncRoleplayTranscriptAsync(sessionId);

        var savedTranscript = await storyStore.ReadAsync("demo-campaign", "scene-01.md");
        Assert.Contains("## Turn 1 - User\n\nI knock twice.", savedTranscript);
        Assert.Contains("## Turn 1 - Nadia\n\nNadia opens the peephole and lowers her voice.", savedTranscript);
        Assert.DoesNotContain("_No roleplay turns have been synced yet._", savedTranscript);
    }

    [Fact]
    public async Task List_ReturnsSavedSessions()
    {
        var tree1 = new ConversationTree(Guid.CreateVersion7(), "Session 1",
            _loggerFactory.CreateLogger<ConversationTree>());
        var tree2 = new ConversationTree(Guid.CreateVersion7(), "Session 2",
            _loggerFactory.CreateLogger<ConversationTree>());

        tree1.Append(tree1.RootId, "user", new MessageContent("Hello"));
        tree1.Append(tree1.ActiveLeafId, "assistant", new MessageContent("Hi there"));
        tree2.Append(tree2.RootId, "user", new MessageContent("Only one"));

        await _store.SaveAsync(tree1);
        await _store.SaveAsync(tree2);

        var list = await _store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Name == "Session 1");
        Assert.Contains(list, s => s.Name == "Session 2");
        Assert.Contains(list, s => s.Name == "Session 1" && s.MessageCount == 2);
        Assert.Contains(list, s => s.Name == "Session 2" && s.MessageCount == 1);
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var tree = new ConversationTree(Guid.CreateVersion7(), "To Delete",
            _loggerFactory.CreateLogger<ConversationTree>());
        await _store.SaveAsync(tree);

        await _store.DeleteAsync(tree.SessionId);

        var list = await _store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task Load_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _store.LoadAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task LegacyMigration_ConvertsToTree()
    {
        // Write a legacy format session file directly
        var sessionId = Guid.CreateVersion7();
        var legacyJson = $$"""
            {
                "format": "legacy",
                "name": "Old Session",
                "messages": [
                    {"role": "user", "content": "Hello from the past"},
                    {"role": "assistant", "content": "Greetings, time traveler"}
                ]
            }
            """;

        var path = Path.Combine(_tempDir, $"{sessionId}.json");
        await File.WriteAllTextAsync(path, legacyJson);

        var loaded = await _store.LoadAsync(sessionId);

        Assert.Equal("Old Session", loaded.Name);
        var thread = loaded.ToFlatThread();
        Assert.Equal(2, thread.Count);
        Assert.Equal("Hello from the past", thread[0].Content.GetText());
        Assert.Equal("Greetings, time traveler", thread[1].Content.GetText());
    }

    [Fact]
    public async Task SavedFile_IsValidJson()
    {
        var tree = new ConversationTree(Guid.CreateVersion7(), "JSON Test",
            _loggerFactory.CreateLogger<ConversationTree>());
        tree.Append(tree.RootId, "user", new MessageContent("test"));

        await _store.SaveAsync(tree);

        var path = Path.Combine(_tempDir, $"{tree.SessionId}.json");
        var json = await File.ReadAllTextAsync(path);

        // Should parse without error
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("tree", doc.RootElement.GetProperty("format").GetString());
    }
}
