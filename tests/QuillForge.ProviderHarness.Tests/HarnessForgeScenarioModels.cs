namespace QuillForge.ProviderHarness.Tests;

public sealed record HarnessForgeScenarioReport
{
    public required string ScenarioName { get; init; }
    public required string ProjectName { get; init; }
    public required IReadOnlyList<HarnessForgePhaseReport> Phases { get; init; }
}

public sealed record HarnessForgePhaseReport
{
    public required string PhaseName { get; init; }
    public required DualSidedHarnessRun Run { get; init; }
    public required HarnessEvaluationResult Evaluation { get; init; }
}
