using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class ToolArgsTests
{
    [Fact]
    public void Parse_MapsSnakeCaseProperties()
    {
        var input = new ToolInput(JsonDocument.Parse(
            """
            {
                "scene_description": "A duel at dawn.",
                "tone_notes": "tense"
            }
            """).RootElement);

        var args = ToolArgs<SnakeCaseArgs>.Parse(input);

        Assert.Equal("A duel at dawn.", args.SceneDescription);
        Assert.Equal("tense", args.ToneNotes);
    }

    [Fact]
    public void Parse_InvalidTypedShape_ThrowsToolArgsParseException()
    {
        var input = new ToolInput(JsonDocument.Parse("""{"count":"not-a-number"}""").RootElement);

        var ex = Assert.Throws<ToolArgsParseException>(() => ToolArgs<NumericArgs>.Parse(input));

        Assert.Contains("NumericArgs", ex.Message);
    }

    private sealed record NumericArgs
    {
        public int Count { get; init; }
    }

    private sealed record SnakeCaseArgs
    {
        public string SceneDescription { get; init; } = "";
        public string? ToneNotes { get; init; }
    }
}
