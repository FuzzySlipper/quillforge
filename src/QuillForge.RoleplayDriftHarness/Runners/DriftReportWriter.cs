using System.Globalization;
using System.Text.Json;
using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Runners;

/// <summary>
/// Writes drift harness artifacts to disk:
/// - run.json — complete run with all events and drift results
/// - trace.ndjson — line-delimited JSON trace events
/// - evaluation.json — top-level evaluation result
/// - summary.md — human-readable summary
/// - lore-results.json — knowledge packet structured payloads (aligned with #1661)
/// </summary>
public sealed class DriftReportWriter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions s_jsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Write all artifacts for a drift harness run.
    /// </summary>
    public void WriteAll(string outputDir, DriftHarnessRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Directory.CreateDirectory(outputDir);

        WriteRunJson(outputDir, run);
        WriteTraceNdjson(outputDir, run);
        WriteEvaluationJson(outputDir, run);
        WriteSummaryMd(outputDir, run);
        WriteLoreResultsJson(outputDir, run);
    }

    private static void WriteRunJson(string outputDir, DriftHarnessRun run)
    {
        var path = Path.Combine(outputDir, "run.json");
        var payload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            active_character = run.ActiveCharacter,
            off_character = run.OffCharacter,
            turns = run.Turns.Select(t => new
            {
                turn_number = t.TurnNumber,
                user_message = t.UserMessage,
                boundary_count = t.Boundaries.Count,
            }),
            forbidden_details = run.ForbiddenDetails,
            started_at = run.StartedAt.ToString("O", CultureInfo.InvariantCulture),
            completed_at = run.CompletedAt?.ToString("O", CultureInfo.InvariantCulture),
            trace_event_count = run.TraceEvents.Count,
            drift_result = new
            {
                has_drift = run.DriftResult.HasDrift,
                finding_count = run.DriftResult.Findings.Count,
                findings = run.DriftResult.Findings.Select(f => new
                {
                    forbidden_fact = f.ForbiddenFact,
                    first_appearance_turn = f.FirstAppearanceTurn,
                    first_appearance_boundary = f.FirstAppearanceBoundary,
                    first_appearance_component = f.FirstAppearanceComponent,
                    likely_origin = f.LikelyOrigin,
                    evidence = f.Evidence,
                }),
            },
            evaluation = run.Evaluation is null ? null : new
            {
                passed = run.Evaluation.Passed,
                total_turns = run.Evaluation.TotalTurns,
                total_events = run.Evaluation.TotalEvents,
                drift_count = run.Evaluation.DriftCount,
                expected_drift_count = run.Evaluation.ExpectedDriftCount,
                origins = run.Evaluation.Origins,
                notes = run.Evaluation.Notes,
            },
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, s_jsonOptions));
    }

    private static void WriteTraceNdjson(string outputDir, DriftHarnessRun run)
    {
        var path = Path.Combine(outputDir, "trace.ndjson");
        using var writer = new StreamWriter(path);
        foreach (var evt in run.TraceEvents)
        {
            var payload = new
            {
                turn = evt.Turn,
                component = evt.Component,
                boundary = evt.Boundary,
                agent = evt.Agent,
                provider = evt.Provider,
                model = evt.Model,
                timestamp = evt.Timestamp?.ToString("O", CultureInfo.InvariantCulture),
                duration_ms = evt.DurationMs,
                source_refs = evt.SourceRefs,
                preview = TruncateForSerialization(evt.Preview, 200),
                content = TruncateForSerialization(evt.Content, 5000),
                structured_payload = evt.StructuredPayload is null ? null : new
                {
                    active_subject = evt.StructuredPayload.ActiveSubject,
                    applicability = evt.StructuredPayload.Applicability,
                    allowed_use = evt.StructuredPayload.AllowedUse,
                    lore_refs = evt.StructuredPayload.LoreRefs,
                    source_component = evt.StructuredPayload.SourceComponent,
                },
            };
            writer.WriteLine(JsonSerializer.Serialize(payload, s_jsonlOptions));
        }
    }

    private static void WriteEvaluationJson(string outputDir, DriftHarnessRun run)
    {
        var path = Path.Combine(outputDir, "evaluation.json");
        var payload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            passed = run.Evaluation?.Passed,
            total_turns = run.Evaluation?.TotalTurns,
            total_events = run.Evaluation?.TotalEvents,
            drift_count = run.Evaluation?.DriftCount,
            origins = run.Evaluation?.Origins,
            notes = run.Evaluation?.Notes,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, s_jsonOptions));
    }

    private static void WriteSummaryMd(string outputDir, DriftHarnessRun run)
    {
        var path = Path.Combine(outputDir, "summary.md");
        using var writer = new StreamWriter(path);

        writer.WriteLine("# Roleplay Drift Harness Summary");
        writer.WriteLine();
        writer.WriteLine($"- **Run ID**: `{run.RunId}`");
        writer.WriteLine($"- **Scenario**: {run.ScenarioName}");
        writer.WriteLine($"- **Active Character**: {run.ActiveCharacter}");
        writer.WriteLine($"- **Off-Character**: {run.OffCharacter}");
        writer.WriteLine($"- **Total Turns**: {run.Turns.Count}");
        writer.WriteLine($"- **Total Trace Events**: {run.TraceEvents.Count}");
        writer.WriteLine($"- **Passed**: {run.Evaluation?.Passed ?? false}");
        writer.WriteLine();

        if (run.DriftResult.HasDrift)
        {
            writer.WriteLine("## Drift Detected");
            writer.WriteLine();
            writer.WriteLine($"Found **{run.DriftResult.Findings.Count}** forbidden detail(s) in the trace.");
            writer.WriteLine();
            writer.WriteLine("| Forbidden Fact | First Turn | Boundary | Component | Likely Origin |");
            writer.WriteLine("|---------------|------------|----------|-----------|---------------|");
            foreach (var f in run.DriftResult.Findings)
            {
                writer.WriteLine($"| {f.ForbiddenFact} | {f.FirstAppearanceTurn} | {f.FirstAppearanceBoundary} | {f.FirstAppearanceComponent} | {f.LikelyOrigin} |");
            }
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine("## No Drift Detected");
            writer.WriteLine();
            writer.WriteLine("All forbidden details were properly excluded from the output trace.");
            writer.WriteLine();
        }

        if (run.Evaluation?.Origins is not null && run.Evaluation.Origins.Count > 0)
        {
            writer.WriteLine("## Origin Breakdown");
            writer.WriteLine();
            foreach (var (origin, count) in run.Evaluation.Origins)
            {
                writer.WriteLine($"- **{origin}**: {count}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Trace Events");
        writer.WriteLine();
        writer.WriteLine("| # | Turn | Boundary | Component | Preview |");
        writer.WriteLine("|---|------|----------|-----------|---------|");
        var idx = 0;
        foreach (var evt in run.TraceEvents)
        {
            idx++;
            var preview = evt.Preview.Length > 60 ? evt.Preview[..60] + "…" : evt.Preview;
            writer.WriteLine($"| {idx} | {evt.Turn} | {evt.Boundary} | {evt.Component} | {SanitizeMd(preview)} |");
        }
        writer.WriteLine();

        writer.WriteLine("## Artifacts");
        writer.WriteLine();
        writer.WriteLine("| File | Description |");
        writer.WriteLine("|------|-------------|");
        writer.WriteLine("| `run.json` | Complete run with events, drift results, and evaluation |");
        writer.WriteLine("| `trace.ndjson` | Line-delimited JSON trace events (one per boundary) |");
        writer.WriteLine("| `evaluation.json` | Top-level evaluation summary |");
        writer.WriteLine("| `lore-results.json` | Structured knowledge packets in #1661-compatible format |");
        writer.WriteLine("| `summary.md` | This human-readable summary |");
        writer.WriteLine();

        writer.WriteLine("---");
        writer.WriteLine($"Report generated at {DateTimeOffset.UtcNow:O}");
    }

    private static void WriteLoreResultsJson(string outputDir, DriftHarnessRun run)
    {
        var path = Path.Combine(outputDir, "lore-results.json");

        var packets = run.TraceEvents
            .Where(e => e.StructuredPayload is not null)
            .Select(e => new
            {
                turn = e.Turn,
                boundary = e.Boundary,
                component = e.Component,
                preview = TruncateForSerialization(e.Preview, 200),
                active_subject = e.StructuredPayload!.ActiveSubject,
                applicability = e.StructuredPayload.Applicability,
                allowed_use = e.StructuredPayload.AllowedUse,
                lore_refs = e.StructuredPayload.LoreRefs,
                source_component = e.StructuredPayload.SourceComponent,
                first_known_turn = e.Turn,
            })
            .ToList();

        var payload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            knowledge_packets = packets,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, s_jsonOptions));
    }

    private static string TruncateForSerialization(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;
        return value.Length <= maxLen ? value : value[..maxLen] + "…[truncated]";
    }

    private static string SanitizeMd(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
