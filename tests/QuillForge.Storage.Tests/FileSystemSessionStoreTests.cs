using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
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
        tree.Append(tree.ActiveLeafId, "assistant", new MessageContent("Hi there!"));

        await _store.SaveAsync(tree);
        var loaded = await _store.LoadAsync(tree.SessionId);

        Assert.Equal(tree.SessionId, loaded.SessionId);
        Assert.Equal("Test Session", loaded.Name);
        Assert.Equal(tree.Count, loaded.Count);

        var thread = loaded.ToFlatThread();
        Assert.Equal(2, thread.Count);
        Assert.Equal("Hello!", thread[0].Content.GetText());
        Assert.Equal("Hi there!", thread[1].Content.GetText());
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
