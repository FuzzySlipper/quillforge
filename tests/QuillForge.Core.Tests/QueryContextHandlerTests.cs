using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public sealed class QueryContextHandlerTests
{
    [Fact]
    public async Task QueryContextHandler_ReturnsCharacterCardSourceWithoutLibrarianLookup()
    {
        var handler = new QueryContextHandler(
            new FakeInteractiveSessionContextService(),
            new ConfigurableLoreStore(new Dictionary<string, string>
            {
                ["cities/veyr.md"] = "Veyr is famous for seven moon gates.",
            }),
            NullLogger<QueryContextHandler>.Instance);
        using var input = JsonDocument.Parse("""{"query": "Nadia moon key"}""");
        var context = new AgentContext
        {
            SessionId = Guid.CreateVersion7(),
            ActiveMode = Mode.Roleplay,
            ActiveLoreSet = "builder",
            SessionContext = new InteractiveSessionContext
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "gatehouse",
                StoryStatePath = "gatehouse/.state.yaml",
                Character = "nadia",
                CharacterSection = "Nadia is a careful archivist who carries the moon key.",
            },
        };

        var result = await handler.HandleAsync(new ToolInput(input.RootElement), context);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.Content);
        var results = output.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, item =>
            item.GetProperty("source_type").GetString() == "character_card"
            && item.GetProperty("snippet").GetString()!.Contains("moon key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryContextHandler_LabelsLoreDocumentMatches()
    {
        var handler = new QueryContextHandler(
            new FakeInteractiveSessionContextService(),
            new ConfigurableLoreStore(new Dictionary<string, string>
            {
                ["cities/veyr.md"] = "Veyr is famous for seven moon gates.",
            }),
            NullLogger<QueryContextHandler>.Instance);
        using var input = JsonDocument.Parse("""{"query": "moon gates"}""");

        var result = await handler.HandleAsync(
            new ToolInput(input.RootElement),
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Writer,
                ActiveLoreSet = "builder",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveMode = Mode.Writer,
                    ProjectName = "gatehouse",
                    StoryStatePath = "gatehouse/.state.yaml",
                },
            });

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.Content);
        var results = output.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, item =>
            item.GetProperty("source_type").GetString() == "lore_document"
            && item.GetProperty("source_id").GetString() == "cities/veyr.md");
    }
}
