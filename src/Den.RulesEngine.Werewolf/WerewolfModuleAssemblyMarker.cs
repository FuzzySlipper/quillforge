using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf;

/// <summary>
/// Assembly marker for the first explicitly registered rules-engine module project.
/// </summary>
/// <remarks>
/// Werewolf behavior lands in a later task. This marker makes the module assembly
/// visible to solution and boundary tests without adding runtime discovery.
/// </remarks>
public static class WerewolfModuleAssemblyMarker
{
    public const string ProjectName = "Den.RulesEngine.Werewolf";

    public static Type RulesEngineMarkerType => typeof(RulesEngineAssemblyMarker);

    public static string RulesEngineProjectName =>
        RulesEngineMarkerType.Assembly.GetName().Name ?? RulesEngineAssemblyMarker.ProjectName;
}
