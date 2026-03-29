using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class LibrarianParsingTests
{
    [Fact]
    public void ParsesCleanJson()
    {
        var json = """
            {
                "relevant_passages": ["Elena is a warrior", "She carries a silver blade"],
                "source_files": ["characters/elena.md"],
                "confidence": "high"
            }
            """;

        var bundle = LibrarianAgent.ParseLoreBundle(json);

        Assert.Equal(2, bundle.RelevantPassages.Count);
        Assert.Single(bundle.SourceFiles);
        Assert.Equal(LoreConfidence.High, bundle.Confidence);
    }

    [Fact]
    public void ParsesJsonInMarkdownFences()
    {
        var text = """
            Here is the result:
            ```json
            {
                "relevant_passages": ["The castle stands tall"],
                "source_files": ["locations/castle.md"],
                "confidence": "medium"
            }
            ```
            """;

        var bundle = LibrarianAgent.ParseLoreBundle(text);

        Assert.Single(bundle.RelevantPassages);
        Assert.Equal(LoreConfidence.Medium, bundle.Confidence);
    }

    [Fact]
    public void ParsesJsonEmbeddedInProse()
    {
        var text = """
            Based on my search of the lore, here are the results:
            {"relevant_passages": ["The dragon sleeps"], "source_files": ["creatures/dragon.md"], "confidence": "high"}
            I hope this helps!
            """;

        var bundle = LibrarianAgent.ParseLoreBundle(text);

        Assert.Single(bundle.RelevantPassages);
        Assert.Equal("The dragon sleeps", bundle.RelevantPassages[0]);
    }

    [Fact]
    public void FallsBackToRawText_WhenNoJson()
    {
        var text = "I couldn't find any structured data but the elves live in the forest.";

        var bundle = LibrarianAgent.ParseLoreBundle(text);

        Assert.Single(bundle.RelevantPassages);
        Assert.Equal(text, bundle.RelevantPassages[0]);
        Assert.Empty(bundle.SourceFiles);
        Assert.Equal(LoreConfidence.Low, bundle.Confidence);
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyBundle()
    {
        var bundle = LibrarianAgent.ParseLoreBundle("   ");

        Assert.Empty(bundle.RelevantPassages);
        Assert.Equal(LoreConfidence.Low, bundle.Confidence);
    }

    [Fact]
    public void MissingFields_DefaultGracefully()
    {
        var json = """{"relevant_passages": ["something"]}""";

        var bundle = LibrarianAgent.ParseLoreBundle(json);

        Assert.Single(bundle.RelevantPassages);
        Assert.Empty(bundle.SourceFiles);
        Assert.Equal(LoreConfidence.High, bundle.Confidence);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesAllLoreFiles()
    {
        var lore = new Dictionary<string, string>
        {
            ["characters/elena.md"] = "Elena is brave.",
            ["locations/castle.md"] = "The castle is ancient.",
        };

        var prompt = LibrarianAgent.BuildSystemPrompt(lore, "test-lore");

        Assert.Contains("### File: characters/elena.md", prompt);
        Assert.Contains("Elena is brave.", prompt);
        Assert.Contains("### File: locations/castle.md", prompt);
        Assert.Contains("The castle is ancient.", prompt);
        Assert.Contains("Librarian", prompt);
        Assert.Contains("test-lore", prompt);
    }
}
