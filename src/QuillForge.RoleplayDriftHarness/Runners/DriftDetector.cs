using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Runners;

/// <summary>
/// Scans trace events for forbidden facts and classifies the likely origin
/// of each drift occurrence. Purely structural — no LLM evaluator required.
/// </summary>
public sealed class DriftDetector
{
    /// <summary>
    /// Detect drift in a set of trace events against a list of forbidden details.
    /// </summary>
    public DriftDetectionResult Detect(
        IReadOnlyList<TraceEvent> traceEvents,
        IReadOnlyList<string> forbiddenDetails)
    {
        ArgumentNullException.ThrowIfNull(traceEvents);
        ArgumentNullException.ThrowIfNull(forbiddenDetails);

        if (forbiddenDetails.Count == 0)
        {
            return new DriftDetectionResult { HasDrift = false };
        }

        var findings = new List<DriftFinding>();

        foreach (var fact in forbiddenDetails)
        {
            var factLower = fact.ToLowerInvariant();

            // Find the first trace event that contains this forbidden fact
            var firstEvent = traceEvents.FirstOrDefault(
                e => e.Content is not null &&
                     e.Content.Contains(factLower, StringComparison.OrdinalIgnoreCase));

            if (firstEvent is null)
            {
                // Also check preview for shorter content
                firstEvent = traceEvents.FirstOrDefault(
                    e => e.Preview.Contains(factLower, StringComparison.OrdinalIgnoreCase));
            }

            if (firstEvent is not null)
            {
                var origin = ClassifyOrigin(firstEvent, traceEvents);
                findings.Add(new DriftFinding
                {
                    ForbiddenFact = fact,
                    FirstAppearanceTurn = firstEvent.Turn,
                    FirstAppearanceBoundary = firstEvent.Boundary,
                    FirstAppearanceComponent = firstEvent.Component,
                    LikelyOrigin = origin,
                    Evidence = $"First detected in event at turn {firstEvent.Turn}, boundary '{firstEvent.Boundary}', component '{firstEvent.Component}'",
                });
            }
        }

        return new DriftDetectionResult
        {
            HasDrift = findings.Count > 0,
            Findings = findings,
        };
    }

    /// <summary>
    /// Classify the likely origin of a forbidden fact based on which boundary
    /// type it first appeared in.
    /// </summary>
    private static string ClassifyOrigin(TraceEvent firstEvent, IReadOnlyList<TraceEvent> allEvents)
    {
        // If the first appearance is at a QueryLore boundary, it likely came from retrieval
        if (firstEvent.Boundary == nameof(BoundaryType.QueryLore))
            return "retrieval";

        // If NarrativeDirector, it was synthesized from context
        if (firstEvent.Boundary == nameof(BoundaryType.NarrativeDirector))
            return "director_synthesis";

        // If ProseWriter, it was introduced during prose generation
        if (firstEvent.Boundary == nameof(BoundaryType.ProseWriter))
            return "prose_misuse";

        // If VisibleResponse but not in earlier boundaries, check if it appeared
        // only in the visible output (prose writer didn't filter it)
        if (firstEvent.Boundary == nameof(BoundaryType.VisibleResponse))
        {
            // Check if the fact existed in any earlier boundary
            var earlierHasFact = allEvents
                .Where(e => e.Turn <= firstEvent.Turn &&
                           (e.Component == "query_lore" || e.Component == "director" || e.Component == "prose_writer"))
                .Any(e => e.Content is not null &&
                          e.Content.Contains(firstEvent.Preview.Length > 20
                              ? firstEvent.Preview[..20]
                              : firstEvent.Preview, StringComparison.OrdinalIgnoreCase));

            if (!earlierHasFact)
                return "visible_response";

            return "prose_misuse";
        }

        if (firstEvent.Boundary == nameof(BoundaryType.SummaryHistory))
            return "summary_history";

        // Fallback: look at the component name for evidence
        return firstEvent.Component switch
        {
            "query_lore" or "librarian" => "retrieval",
            "director" or "scene_brief" or "narrative_director" => "director_synthesis",
            "prose_writer" or "direct_scene" or "prose" => "prose_misuse",
            "visible_response" or "assistant_response" => "visible_response",
            "summary" or "history" or "memory" => "summary_history",
            _ => "uncertain",
        };
    }
}
