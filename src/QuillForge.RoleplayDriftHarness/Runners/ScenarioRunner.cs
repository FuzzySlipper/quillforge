using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Runners;

/// <summary>
/// Runs a scripted roleplay scenario through the drift harness,
/// producing trace events and drift detection results.
/// This is the core execution engine — it deterministically simulates
/// component boundary outputs and records them as trace events.
/// No live LLM is required for the baseline deterministic mode.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly DriftDetector _driftDetector;

    public ScenarioRunner(DriftDetector driftDetector)
    {
        _driftDetector = driftDetector;
    }

    /// <summary>
    /// Run a scripted scenario and produce the full harness run result.
    /// </summary>
    public DriftHarnessRun Run(RoleplayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        var traceEvents = new List<TraceEvent>();
        var turnIndex = 0;

        foreach (var turn in scenario.Turns)
        {
            turnIndex++;

            // 1. Record user/driver turn
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "user_turn",
                Boundary = nameof(BoundaryType.UserTurn),
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100),
                Preview = Truncate(turn.UserMessage, 120),
                Content = turn.UserMessage,
            });

            // 2. Process each boundary output in order
            foreach (var boundary in turn.Boundaries)
            {
                var boundaryTimestamp = startedAt.AddMilliseconds(turnIndex * 100 + 50);
                var component = string.IsNullOrWhiteSpace(boundary.Component)
                    ? boundary.Boundary
                    : boundary.Component;

                traceEvents.Add(new TraceEvent
                {
                    Turn = turn.TurnNumber,
                    Component = component,
                    Boundary = boundary.Boundary,
                    Agent = MapBoundaryToAgent(boundary.Boundary),
                    Timestamp = boundaryTimestamp,
                    SourceRefs = boundary.SourceRefs,
                    Preview = Truncate(boundary.Content, 120),
                    Content = boundary.Content,
                    StructuredPayload = boundary.Payload,
                });
            }
        }

        var completedAt = DateTimeOffset.UtcNow;

        // 3. Run drift detection
        var driftResult = _driftDetector.Detect(traceEvents, scenario.ForbiddenDetails);

        // 4. Build evaluation
        var origins = driftResult.Findings
            .GroupBy(f => f.LikelyOrigin)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var evaluation = new DriftRunEvaluation
        {
            Passed = !driftResult.HasDrift,
            TotalTurns = scenario.Turns.Count,
            TotalEvents = traceEvents.Count,
            DriftCount = driftResult.Findings.Count,
            Origins = origins.Count > 0 ? origins : null,
        };

        return new DriftHarnessRun
        {
            RunId = runId,
            ScenarioName = scenario.Name,
            ActiveCharacter = scenario.ActiveCharacter,
            OffCharacter = scenario.OffCharacter,
            Turns = scenario.Turns,
            ForbiddenDetails = scenario.ForbiddenDetails,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            TraceEvents = traceEvents,
            DriftResult = driftResult,
            Evaluation = evaluation,
        };
    }

    private static string? MapBoundaryToAgent(string boundary)
    {
        return boundary switch
        {
            nameof(BoundaryType.QueryLore) => "LibrarianAgent",
            nameof(BoundaryType.NarrativeDirector) => "NarrativeDirector",
            nameof(BoundaryType.ProseWriter) => "ProseWriter",
            nameof(BoundaryType.VisibleResponse) => "ProseWriter",
            nameof(BoundaryType.SummaryHistory) => "MemoryManager",
            _ => null,
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;
        var normalized = value.Replace("\r", "", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "…";
    }
}
