using YamlDotNet.Serialization;

namespace QuillForge.Architecture.Tests.Scenarios;

/// <summary>
/// A YAML-defined multi-turn test scenario driven through in-process services.
/// </summary>
public sealed class ScenarioDefinition
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Profile { get; set; }
    public List<ScenarioStep> Steps { get; set; } = [];
}

/// <summary>
/// A single step in a scenario. Exactly one action field should be set.
/// </summary>
public sealed class ScenarioStep
{
    /// <summary>Send a chat message and stream the response.</summary>
    public string? Chat { get; set; }

    /// <summary>Switch the session mode.</summary>
    [YamlMember(Alias = "set_mode")]
    public string? SetMode { get; set; }

    /// <summary>Set the session profile.</summary>
    [YamlMember(Alias = "set_profile")]
    public string? SetProfile { get; set; }

    /// <summary>Persist and reload the session from disk.</summary>
    [YamlMember(Alias = "reload_session")]
    public bool ReloadSession { get; set; }

    /// <summary>Assertions to check after this step completes.</summary>
    public StepExpectation? Expect { get; set; }
}

/// <summary>
/// Assertions evaluated after a scenario step. All non-null fields are checked.
/// </summary>
public sealed class StepExpectation
{
    /// <summary>Expected mode after this step.</summary>
    public string? Mode { get; set; }

    /// <summary>Expected writer state (idle, pendingreview).</summary>
    [YamlMember(Alias = "writer_state")]
    public string? WriterState { get; set; }

    /// <summary>At least one tool call with this name should have occurred.</summary>
    [YamlMember(Alias = "tool_called")]
    public string? ToolCalled { get; set; }

    /// <summary>Final content should contain this substring (case-insensitive).</summary>
    [YamlMember(Alias = "content_contains")]
    public string? ContentContains { get; set; }

    /// <summary>Total message count should be at least this.</summary>
    [YamlMember(Alias = "message_count_gte")]
    public int? MessageCountGte { get; set; }

    /// <summary>Stop reason should equal this value.</summary>
    [YamlMember(Alias = "stop_reason")]
    public string? StopReasonValue { get; set; }

    /// <summary>Content should not be empty.</summary>
    [YamlMember(Alias = "has_content")]
    public bool? HasContent { get; set; }
}
