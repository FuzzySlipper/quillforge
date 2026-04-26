using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class FileOperationHandlerTests
{
    [Fact]
    public async Task WriteFileHandler_BlocksDirectLoreWrites()
    {
        var files = new FakeContentFileService();
        var handler = new WriteFileHandler(files, NullLogger<WriteFileHandler>.Instance);
        using var document = JsonDocument.Parse(
            """
            {
              "directory": "lore",
              "path": "history.md",
              "content": "forbidden"
            }
            """);

        var result = await handler.HandleAsync(new ToolInput(document.RootElement), new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Guide });

        Assert.False(result.Success);
        Assert.Contains("Lore Builder", result.Error);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task SaveLoreFileHandler_WritesOnlyInLoreBuilderMode()
    {
        var files = new FakeContentFileService();
        var handler = new SaveLoreFileHandler(files, NullLogger<SaveLoreFileHandler>.Instance);
        using var document = JsonDocument.Parse(
            """
            {
              "target_file_path": "factions/silverwatch",
              "content": "# Silverwatch\n\n- Guards the north road.",
              "user_confirmed": true,
              "confirmation_note": "User asked to save this lore."
            }
            """);

        var result = await handler.HandleAsync(
            new ToolInput(document.RootElement),
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Lore,
                ActiveLoreSet = "builder",
            });

        Assert.True(result.Success);
        Assert.True(files.Files.TryGetValue("lore/builder/factions/silverwatch.md", out var saved));
        Assert.Contains("Guards the north road", saved);
    }

    [Fact]
    public async Task SaveLoreFileHandler_RequiresUserConfirmation()
    {
        var files = new FakeContentFileService();
        var handler = new SaveLoreFileHandler(files, NullLogger<SaveLoreFileHandler>.Instance);
        using var document = JsonDocument.Parse(
            """
            {
              "target_file_path": "history.md",
              "content": "draft",
              "user_confirmed": false
            }
            """);

        var result = await handler.HandleAsync(
            new ToolInput(document.RootElement),
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Lore,
                ActiveLoreSet = "builder",
            });

        Assert.False(result.Success);
        Assert.Contains("explicitly request or approve", result.Error);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task SaveLoreFileHandler_BlocksPathTraversal()
    {
        var files = new FakeContentFileService();
        var handler = new SaveLoreFileHandler(files, NullLogger<SaveLoreFileHandler>.Instance);
        using var document = JsonDocument.Parse(
            """
            {
              "target_file_path": "../outside.md",
              "content": "draft",
              "user_confirmed": true
            }
            """);

        var result = await handler.HandleAsync(
            new ToolInput(document.RootElement),
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Lore,
                ActiveLoreSet = "builder",
            });

        Assert.False(result.Success);
        Assert.Contains("cannot traverse", result.Error);
        Assert.Empty(files.Files);
    }
}
