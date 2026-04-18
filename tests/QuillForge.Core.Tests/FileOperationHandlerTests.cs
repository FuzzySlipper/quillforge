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
        Assert.Contains("/canonize", result.Error);
        Assert.Empty(files.Files);
    }
}
