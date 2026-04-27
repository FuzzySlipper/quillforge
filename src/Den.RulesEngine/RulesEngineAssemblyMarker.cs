namespace Den.RulesEngine;

/// <summary>
/// Assembly marker for the portable rules engine project.
/// </summary>
/// <remarks>
/// Task #830 establishes the project boundary. Core contracts and behavior land in
/// later tasks after the boundary is locked by tests.
/// </remarks>
public static class RulesEngineAssemblyMarker
{
    public const string ProjectName = "Den.RulesEngine";
}
