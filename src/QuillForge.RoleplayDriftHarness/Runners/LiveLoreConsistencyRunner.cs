using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Runners;

/// <summary>
/// Runs a live LLM-backed Xavier/Caleb lore consistency session through
/// the actual roleplay agent pipeline, capturing diagnostics at every
/// component boundary.
///
/// This is the "live" counterpart to the deterministic ScenarioRunner.
/// It uses a configured ICompletionService to execute actual LLM calls,
/// then feeds the outputs through the same DriftDetector for analysis.
///
/// The test is CI-safe: skipped when no provider is configured, gated
/// by both environment variables and explicit opt-in flags.
/// </summary>
public sealed class LiveLoreConsistencyRunner
{
    private readonly ICompletionService _completionService;
    private readonly DriftDetector _driftDetector;
    private readonly string _provider;
    private readonly string _model;

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
    /// Create a live lore consistency runner.
    /// </summary>
    /// <param name="completionService">The LLM completion service to use for roleplay agent calls.</param>
    /// <param name="driftDetector">The drift detector for analyzing outputs.</param>
    /// <param name="provider">Provider alias (e.g. "openai", "anthropic", "openrouter").</param>
    /// <param name="model">Model name (e.g. "gpt-4o", "claude-sonnet-4", "qwen3-35b").</param>
    public LiveLoreConsistencyRunner(
        ICompletionService completionService,
        DriftDetector driftDetector,
        string provider,
        string model)
    {
        _completionService = completionService ?? throw new ArgumentNullException(nameof(completionService));
        _driftDetector = driftDetector ?? throw new ArgumentNullException(nameof(driftDetector));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>
    /// Run the live Xavier/Caleb lore consistency session and produce
    /// a DriftHarnessRun with actual LLM outputs at each boundary.
    /// </summary>
    /// <param name="outputDir">Directory for diagnostic artifacts.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DriftHarnessRun> RunAsync(string outputDir, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var traceEvents = new List<TraceEvent>();
        var turnIndex = 0;

        // Forbidden details to detect
        var forbiddenDetails = new List<string>
        {
            "prosthetic arm",
            "prosthetic",
            "Toring Chip",
            "Toring",
            "custom prosthetic",
        };

        Directory.CreateDirectory(outputDir);

        // Build the system lore context that a real roleplay pipeline would have
        var xavierLoreText = string.Join("\n", LiveXavierCalebScenario.XavierLore);
        var calebLoreText = string.Join("\n", LiveXavierCalebScenario.CalebLore);
        var sharedBodyTechText = string.Join("\n", LiveXavierCalebScenario.SharedBodyTech);

        // Run each probe turn through the actual LLM
        foreach (var turn in LiveXavierCalebScenario.ProbeTurns)
        {
            turnIndex++;
            ct.ThrowIfCancellationRequested();

            Console.WriteLine($"\n=== Live Probe Turn {turn.TurnNumber}: {turn.Category} ===");
            Console.WriteLine($"User: {turn.UserMessage}");

            // Record user turn
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "user_turn",
                Boundary = nameof(BoundaryType.UserTurn),
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100),
                Preview = Truncate(turn.UserMessage, 120),
                Content = turn.UserMessage,
            });

            // ── Boundary 1: QueryLore / LibrarianAgent ──
            Console.WriteLine("\n  [QueryLore/LibrarianAgent]");
            var queryLoreOutput = await CallLibrarianAgent(
                turn, xavierLoreText, calebLoreText, sharedBodyTechText, ct);

            var queryLoreClassification = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                queryLoreOutput, turn.ExpectedSubject, "characters/xavier.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            var queryLorePayload = new StructuredPayload
            {
                ActiveSubject = turn.ExpectedSubject,
                Applicability = queryLoreClassification.Applicability.ToString(),
                AllowedUse = queryLoreClassification.AllowedUse.ToString(),
                LoreRefs = ["characters/xavier.md", "world/body-tech.md"],
                SourceComponent = "query_lore",
            };

            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Agent = "LibrarianAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100 + 50),
                SourceRefs = ["characters/xavier.md", "characters/caleb.md", "world/body-tech.md"],
                Preview = Truncate(queryLoreOutput, 120),
                Content = queryLoreOutput,
                StructuredPayload = queryLorePayload,
            });

            // ── Boundary 2: NarrativeDirector / scene_brief ──
            Console.WriteLine("  [NarrativeDirector/scene_brief]");
            var directorOutput = await CallNarrativeDirector(
                turn, xavierLoreText, queryLoreOutput, ct);

            var directorClassification = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                directorOutput, turn.ExpectedSubject, "characters/xavier.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            var directorPayload = new StructuredPayload
            {
                ActiveSubject = turn.ExpectedSubject,
                Applicability = directorClassification.Applicability.ToString(),
                AllowedUse = directorClassification.AllowedUse.ToString(),
                SourceComponent = "scene_brief",
            };

            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "scene_brief",
                Boundary = nameof(BoundaryType.NarrativeDirector),
                Agent = "NarrativeDirector",
                Provider = _provider,
                Model = _model,
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100 + 100),
                Preview = Truncate(directorOutput, 120),
                Content = directorOutput,
                StructuredPayload = directorPayload,
            });

            // ── Boundary 3: ProseWriter / direct_scene ──
            Console.WriteLine("  [ProseWriter/direct_scene]");
            var proseOutput = await CallProseWriter(
                turn, directorOutput, ct);

            var proseClassification = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                proseOutput, turn.ExpectedSubject, "characters/xavier.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            var prosePayload = new StructuredPayload
            {
                ActiveSubject = turn.ExpectedSubject,
                Applicability = proseClassification.Applicability.ToString(),
                AllowedUse = proseClassification.AllowedUse.ToString(),
                SourceComponent = "direct_scene",
            };

            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "direct_scene",
                Boundary = nameof(BoundaryType.ProseWriter),
                Agent = "ProseWriter",
                Provider = _provider,
                Model = _model,
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100 + 150),
                Preview = Truncate(proseOutput, 120),
                Content = proseOutput,
                StructuredPayload = prosePayload,
            });

            // ── Boundary 4: VisibleResponse ──
            Console.WriteLine("  [VisibleResponse/assistant_response]");
            var visibleOutput = await CallVisibleResponse(
                turn, proseOutput, ct);

            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "visible_response",
                Boundary = nameof(BoundaryType.VisibleResponse),
                Agent = "ProseWriter",
                Provider = _provider,
                Model = _model,
                Timestamp = startedAt.AddMilliseconds(turnIndex * 100 + 200),
                Preview = Truncate(visibleOutput, 120),
                Content = visibleOutput,
            });
        }

        var completedAt = DateTimeOffset.UtcNow;

        // Run drift detection
        var driftResult = _driftDetector.Detect(traceEvents, forbiddenDetails);

        // Build evaluation
        var origins = driftResult.Findings
            .GroupBy(f => f.LikelyOrigin)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var driftCount = driftResult.Findings.Count;
        var evaluation = new DriftRunEvaluation
        {
            Passed = !driftResult.HasDrift,
            TotalTurns = LiveXavierCalebScenario.ProbeTurns.Count,
            TotalEvents = traceEvents.Count,
            DriftCount = driftCount,
            Origins = origins.Count > 0 ? origins : null,
            Notes = driftResult.HasDrift
                ? $"LORE BLEED DETECTED: {driftCount} forbidden fact(s) appeared in the live LLM output. " +
                  $"Provider: {_provider}, Model: {_model}. See findings for details."
                : $"No lore bleed detected with provider={_provider}, model={_model} across {LiveXavierCalebScenario.ProbeTurns.Count} probe turns.",
        };

        var run = new DriftHarnessRun
        {
            RunId = runId,
            ScenarioName = "live-xavier-caleb-lore-consistency",
            ActiveCharacter = "Xavier",
            OffCharacter = "Caleb",
            Turns = LiveXavierCalebScenario.ProbeTurns.Select(t => new ScriptedTurn
            {
                TurnNumber = t.TurnNumber,
                UserMessage = t.UserMessage,
                Boundaries = [],
            }).ToList(),
            ForbiddenDetails = forbiddenDetails,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            TraceEvents = traceEvents,
            DriftResult = driftResult,
            Evaluation = evaluation,
        };

        // Write full diagnostic artifacts
        await WriteLiveDiagnosticsAsync(outputDir, run, completedAt);

        return run;
    }

    /// <summary>
    /// Check whether the live LLM runner can actually make calls.
    /// The ICompletionService itself will throw if the provider isn't configured,
    /// but we provide this for pre-flight checks.
    /// </summary>
    public bool CanReachProvider()
    {
        try
        {
            // Quick connectivity check using a minimal request
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var testRequest = new CompletionRequest
            {
                Model = _model,
                MaxTokens = 1,
                Messages =
                [
                    new CompletionMessage("user", new MessageContent([new TextBlock("ping")])),
                ],
            };
            var response = _completionService.CompleteAsync(testRequest, cts.Token)
                .GetAwaiter().GetResult();
            return response is not null;
        }
        catch
        {
            return false;
        }
    }

    // ── Private roleplay agent call methods ──

    private async Task<string> CallLibrarianAgent(
        LiveProbeTurn turn,
        string xavierLore,
        string calebLore,
        string sharedBodyTech,
        CancellationToken ct)
    {
        var systemPrompt = $"""
You are a LibrarianAgent — a lore retrieval system for a roleplay pipeline.

Your job is to retrieve relevant lore facts about the active character, Xavier.

XAVIER LORE:
{xavierLore}

SHARED WORLD KNOWLEDGE (applies to all operatives, not Xavier specifically):
{sharedBodyTech}

CALEB LORE (OFF-CHARACTER — do NOT attribute any of these to Xavier):
{calebLore}

RULES:
1. Only return lore about Xavier.
2. Caleb's personal details (prosthetic arm, Toring Chip, custom combat plating) belong to Caleb.
   Do NOT mention them as Xavier's — even accidentally.
3. Shared world knowledge (standard neural interfaces, hunter gear) is fine as context.
4. Answer concisely but completely.

User query: {turn.UserMessage}
""";

        var result = await CallLlmAsync(systemPrompt, ct);
        return result;
    }

    private async Task<string> CallNarrativeDirector(
        LiveProbeTurn turn,
        string xavierLore,
        string queryLoreResult,
        CancellationToken ct)
    {
        var systemPrompt = $"""
You are a NarrativeDirector — you synthesize scene briefs and narrative directions
for a roleplay pipeline. You take the lore retrieval result and craft a focused
scene direction.

XAVIER LORE:
{xavierLore}

LORE RETRIEVAL RESULT:
{queryLoreResult}

RULES:
1. Focus the scene direction on Xavier.
2. Do NOT introduce Caleb's personal details (prosthetic arm, Toring Chip).
3. Keep the scene guidance grounded in retrieved lore.

User query context: {turn.UserMessage}
""";

        var result = await CallLlmAsync(systemPrompt, ct);
        return result;
    }

    private async Task<string> CallProseWriter(
        LiveProbeTurn turn,
        string sceneBrief,
        CancellationToken ct)
    {
        var systemPrompt = $"""
You are a ProseWriter — you generate narrative prose from scene briefs.
You are writing about Xavier, a Deepspace Hunter.

SCENE BRIEF / DIRECTION:
{sceneBrief}

RULES:
1. Write vivid narrative prose focused on Xavier.
2. Do NOT attribute Caleb's prosthetic arm, Toring Chip, or custom combat augments to Xavier.
3. Shared standard equipment (neural interfaces, Division gear) is fine.

Output the prose directly, no preamble.
""";

        var result = await CallLlmAsync(systemPrompt, ct);
        return result;
    }

    private async Task<string> CallVisibleResponse(
        LiveProbeTurn turn,
        string proseOutput,
        CancellationToken ct)
    {
        var systemPrompt = $"""
You are finalizing the visible assistant response for a roleplay scene about Xavier.

DRAFT PROSE:
{proseOutput}

RULES:
1. Polish the prose for final output format (roleplay/chat style).
2. Use asterisks for action/description text (*like this*).
3. Do NOT add Caleb-specific details that weren't in the draft.
4. Keep the response focused on Xavier.

Output the final visible response.
""";

        var result = await CallLlmAsync(systemPrompt, ct);
        return result;
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var request = new CompletionRequest
        {
            Model = _model,
            MaxTokens = 1024,
            Temperature = 0.3,
            Messages =
            [
                new CompletionMessage("user", new MessageContent([new TextBlock(prompt)])),
            ],
        };

        var response = await _completionService.CompleteAsync(request, ct);
        sw.Stop();

        var text = string.Join("", response.Content.Blocks
            .OfType<TextBlock>()
            .Select(t => t.Text));

        Console.WriteLine($"    [LLM call: {sw.Elapsed.TotalSeconds:F1}s, {response.Usage.InputTokens}in/{response.Usage.OutputTokens}out]");
        Console.WriteLine($"    Response: {Truncate(text, 200)}");

        return text;
    }

    // ── Artifact writing ──

    /// <summary>
    /// Write full diagnostic artifacts for the live run.
    /// </summary>
    private async Task WriteLiveDiagnosticsAsync(
        string outputDir, DriftHarnessRun run, DateTimeOffset completedAt)
    {
        // Write run.json (extended with live-specific metadata)
        var runJsonPath = Path.Combine(outputDir, "run.json");
        var runPayload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            active_character = run.ActiveCharacter,
            off_character = run.OffCharacter,
            provider = _provider,
            model = _model,
            forbidden_details = run.ForbiddenDetails,
            started_at = run.StartedAt.ToString("O", CultureInfo.InvariantCulture),
            completed_at = completedAt.ToString("O", CultureInfo.InvariantCulture),
            trace_event_count = run.TraceEvents.Count,
            probe_turns = LiveXavierCalebScenario.ProbeTurns.Select(t => new
            {
                turn_number = t.TurnNumber,
                category = t.Category,
                contamination_risk = t.ContaminationRisk,
                user_message = t.UserMessage,
            }),
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
                origins = run.Evaluation.Origins,
                notes = run.Evaluation.Notes,
            },
        };
        var runJson = JsonSerializer.Serialize(runPayload, s_jsonOptions);
        await File.WriteAllTextAsync(runJsonPath, runJson);

        // Write trace.ndjson — one JSON line per trace event with full content
        var tracePath = Path.Combine(outputDir, "trace.ndjson");
        using var traceWriter = new StreamWriter(tracePath);
        foreach (var evt in run.TraceEvents)
        {
            var line = new
            {
                turn = evt.Turn,
                component = evt.Component,
                boundary = evt.Boundary,
                agent = evt.Agent,
                provider = evt.Provider,
                model = evt.Model,
                timestamp = evt.Timestamp?.ToString("O", CultureInfo.InvariantCulture),
                source_refs = evt.SourceRefs,
                preview = evt.Preview,
                content = evt.Content,
                classification_diagnostics = evt.StructuredPayload is null ? null : new
                {
                    active_subject = evt.StructuredPayload.ActiveSubject,
                    applicability = evt.StructuredPayload.Applicability,
                    allowed_use = evt.StructuredPayload.AllowedUse,
                    lore_refs = evt.StructuredPayload.LoreRefs,
                    source_component = evt.StructuredPayload.SourceComponent,
                },
            };
            await traceWriter.WriteLineAsync(JsonSerializer.Serialize(line, s_jsonlOptions));
        }

        // Write evaluation.json
        var evalPath = Path.Combine(outputDir, "evaluation.json");
        var evalPayload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            provider = _provider,
            model = _model,
            passed = run.Evaluation?.Passed,
            total_turns = run.Evaluation?.TotalTurns,
            total_events = run.Evaluation?.TotalEvents,
            drift_count = run.Evaluation?.DriftCount,
            origins = run.Evaluation?.Origins,
            notes = run.Evaluation?.Notes,
        };
        var evalJson = JsonSerializer.Serialize(evalPayload, s_jsonOptions);
        await File.WriteAllTextAsync(evalPath, evalJson);

        // Write summary.md — human-readable
        var summaryPath = Path.Combine(outputDir, "summary.md");
        using var summaryWriter = new StreamWriter(summaryPath);
        await summaryWriter.WriteLineAsync("# Live LLM Lore Consistency Test — Summary");
        await summaryWriter.WriteLineAsync();
        await summaryWriter.WriteLineAsync($"- **Run ID**: `{run.RunId}`");
        await summaryWriter.WriteLineAsync($"- **Provider**: {_provider}");
        await summaryWriter.WriteLineAsync($"- **Model**: {_model}");
        await summaryWriter.WriteLineAsync($"- **Active Character**: {run.ActiveCharacter}");
        await summaryWriter.WriteLineAsync($"- **Off-Character**: {run.OffCharacter}");
        await summaryWriter.WriteLineAsync($"- **Probe Turns**: {LiveXavierCalebScenario.ProbeTurns.Count}");
        await summaryWriter.WriteLineAsync($"- **Passed**: {run.Evaluation?.Passed ?? false}");
        await summaryWriter.WriteLineAsync();

        if (run.DriftResult.HasDrift)
        {
            await summaryWriter.WriteLineAsync("## LORE BLEED DETECTED");
            await summaryWriter.WriteLineAsync();
            await summaryWriter.WriteLineAsync($"Found **{run.DriftResult.Findings.Count}** forbidden detail(s).");
            await summaryWriter.WriteLineAsync();
            await summaryWriter.WriteLineAsync("| Forbidden Fact | First Turn | Boundary | Component | Likely Origin |");
            await summaryWriter.WriteLineAsync("|---------------|------------|----------|-----------|---------------|");
            foreach (var f in run.DriftResult.Findings)
            {
                await summaryWriter.WriteLineAsync($"| {f.ForbiddenFact} | {f.FirstAppearanceTurn} | {f.FirstAppearanceBoundary} | {f.FirstAppearanceComponent} | {f.LikelyOrigin} |");
            }
        }
        else
        {
            await summaryWriter.WriteLineAsync("## No Lore Bleed Detected");
            await summaryWriter.WriteLineAsync();
            await summaryWriter.WriteLineAsync("All probe turns passed: no Caleb-specific forbidden facts appeared in Xavier context.");
        }

        await summaryWriter.WriteLineAsync();
        await summaryWriter.WriteLineAsync("## Probe Turn Details");
        await summaryWriter.WriteLineAsync();
        foreach (var t in LiveXavierCalebScenario.ProbeTurns)
        {
            await summaryWriter.WriteLineAsync($"### Turn {t.TurnNumber}: {t.Category}");
            await summaryWriter.WriteLineAsync($"- **User Message**: {t.UserMessage}");
            await summaryWriter.WriteLineAsync($"- **Contamination Risk**: {t.ContaminationRisk}");
            await summaryWriter.WriteLineAsync();

            var turnEvents = run.TraceEvents.Where(e => e.Turn == t.TurnNumber).ToList();
            foreach (var evt in turnEvents)
            {
                await summaryWriter.WriteLineAsync($"- *{evt.Component}* ({evt.Boundary}): {Truncate(evt.Content ?? "", 300)}");
            }
            await summaryWriter.WriteLineAsync();
        }

        await summaryWriter.WriteLineAsync("---");
        await summaryWriter.WriteLineAsync($"Report generated at {completedAt:O}");

        // Write lore-results.json — structured knowledge packets (#1661 compatible)
        var loreResultsPath = Path.Combine(outputDir, "lore-results.json");
        var packets = run.TraceEvents
            .Where(e => e.StructuredPayload is not null)
            .Select(e => new
            {
                turn = e.Turn,
                boundary = e.Boundary,
                component = e.Component,
                preview = e.Preview,
                active_subject = e.StructuredPayload!.ActiveSubject,
                applicability = e.StructuredPayload.Applicability,
                allowed_use = e.StructuredPayload.AllowedUse,
                lore_refs = e.StructuredPayload.LoreRefs,
                source_component = e.StructuredPayload.SourceComponent,
            })
            .ToList();

        var lorePayload = new
        {
            run_id = run.RunId,
            scenario_name = run.ScenarioName,
            provider = _provider,
            model = _model,
            knowledge_packets = packets,
        };
        var loreJson = JsonSerializer.Serialize(lorePayload, s_jsonOptions);
        await File.WriteAllTextAsync(loreResultsPath, loreJson);

        Console.WriteLine($"\nLive diagnostic artifacts written to: {outputDir}");
        Console.WriteLine($"  run.json, trace.ndjson, evaluation.json, lore-results.json, summary.md");
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;
        var normalized = value.Replace("\r", "", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }
}
