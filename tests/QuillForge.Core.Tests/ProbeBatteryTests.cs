using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class ProbeBatteryTests
{
    private static readonly IReadOnlyList<ToolDefinition> SampleTools =
    [
        new("query_lore", "Query the lore corpus", JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""").RootElement),
        new("write_prose", "Generate prose for a scene", JsonDocument.Parse("""{"type":"object","properties":{"scene_description":{"type":"string"}}}""").RootElement),
        new("roll_dice", "Roll dice", JsonDocument.Parse("""{"type":"object","properties":{"expression":{"type":"string"}}}""").RootElement),
    ];

    [Fact]
    public void Version_IsSet()
    {
        Assert.False(string.IsNullOrEmpty(ProbeBattery.Version));
    }

    [Fact]
    public void Scenarios_HasExpectedCount()
    {
        Assert.Equal(7, ProbeBattery.Scenarios.Count);
    }

    [Fact]
    public void Scenarios_AllHaveUniqueIds()
    {
        var ids = ProbeBattery.Scenarios.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuildProbePrompt_IncludesAllToolNames()
    {
        var prompt = ProbeBattery.BuildProbePrompt("test persona", SampleTools, "general");

        Assert.Contains("`query_lore`", prompt);
        Assert.Contains("`write_prose`", prompt);
        Assert.Contains("`roll_dice`", prompt);
    }

    [Fact]
    public void BuildProbePrompt_IncludesAllScenarioTitles()
    {
        var prompt = ProbeBattery.BuildProbePrompt("test persona", SampleTools, "general");

        foreach (var scenario in ProbeBattery.Scenarios)
        {
            Assert.Contains(scenario.Title, prompt);
        }
    }

    [Fact]
    public void BuildProbePrompt_IncludesModeName()
    {
        var prompt = ProbeBattery.BuildProbePrompt("test persona", SampleTools, "writer");

        Assert.Contains("**writer**", prompt);
    }

    [Fact]
    public void BuildProbePrompt_IncludesSystemPrompt()
    {
        var prompt = ProbeBattery.BuildProbePrompt("You are a creative writing assistant.", SampleTools, "general");

        Assert.Contains("You are a creative writing assistant.", prompt);
    }

    [Fact]
    public void BuildProbePrompt_IncludesAnalysisInstructions()
    {
        var prompt = ProbeBattery.BuildProbePrompt("test", SampleTools, "general");

        Assert.Contains("Initial Assessment", prompt);
        Assert.Contains("Tool Selection", prompt);
        Assert.Contains("Tools Considered but Rejected", prompt);
        Assert.Contains("Mode Boundaries", prompt);
        Assert.Contains("Assumptions", prompt);
    }
}
