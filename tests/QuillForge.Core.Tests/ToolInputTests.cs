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
    public void GetOptionalObjectMap_ConvertsNestedValues()
    {
        var input = new ToolInput(JsonDocument.Parse(
            """
            {
              "updates": {
                "tension": "high",
                "count": 2,
                "flags": [true, false],
                "plot": {
                  "beat": "arrival"
                }
              }
            }
            """).RootElement);

        var updates = input.GetOptionalObjectMap("updates");

        Assert.NotNull(updates);
        Assert.Equal("high", updates["tension"]);
        var count = Assert.IsAssignableFrom<IConvertible>(updates["count"]);
        Assert.Equal(2d, count.ToDouble(null));

        var flags = Assert.IsType<List<object>>(updates["flags"]);
        Assert.Equal([true, false], flags);

        var plot = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(updates["plot"]);
        Assert.Equal("arrival", plot["beat"]);
    }
}
