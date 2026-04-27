using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf;

/// <summary>
/// Assembly marker and module identity constants for the first explicit rules-engine module project.
/// </summary>
public static class WerewolfModuleAssemblyMarker
{
    public const string ProjectName = "Den.RulesEngine.Werewolf";

    public static GameModuleId ModuleId { get; } = new("werewolf");

    public static GameModuleVersion ModuleVersion { get; } = new("0.1.0");

    public static IGameModule CreateModule() => new WerewolfModule();
}
