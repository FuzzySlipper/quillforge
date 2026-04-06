using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public sealed class ToolInputTests
{
    [Fact]
    public void GetOptionalStringList_ReturnsOnlyNonEmptyStrings()
    {
        var input = new ToolInput(JsonDocument.Parse(
            """
            {
              "files_affected": ["src/a.cs", "", 42, "src/b.cs"]
            }
            """).RootElement);

        var values = input.GetOptionalStringList("files_affected");

        Assert.Equal(["src/a.cs", "src/b.cs"], values);
    }

    [Fact]
    public void GetRequiredString_ThrowsOnNullValue()
    {
        var input = new ToolInput(JsonDocument.Parse(
            """
            {
              "name": null
            }
            """).RootElement);

        Assert.Throws<JsonException>(() => input.GetRequiredString("name"));
    }

    [Fact]
    public void GetRequiredString_ThrowsOnMissingProperty()
    {
        var input = new ToolInput(JsonDocument.Parse("{}").RootElement);

        Assert.Throws<JsonException>(() => input.GetRequiredString("name"));
    }
}
