using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Runners;

/// <summary>
/// Strict live lore consistency runner that drives the REAL roleplay session
/// agent pipeline — NarrativeDirectorAgent, ProseWriterAgent, LibrarianAgent,
/// ToolLoop, and all tool handlers — through realistic probe turns.
///
/// This is the #1675 companion to #1673's LiveLoreConsistencyRunner.
/// Key differences:
/// - #1673: simulated pipeline with hand-written boundary prompts
/// - #1675: actual NarrativeDirectorAgent.DirectSceneAsync() with real tools
///
/// The runner constructs the real dependency chain using in-memory stores and
/// the configured ICompletionService, then drives probe questions through the
/// actual NarrativeDirectorAgent, capturing real provenance at each boundary.
/// </summary>
public sealed class StrictRoleplaySessionRunner
{
    private readonly ICompletionService _completionService;
    private readonly DriftDetector _driftDetector;
    private readonly string _provider;
    private readonly string _model;
    private readonly int _ndMaxRounds;
    private readonly int _librarianMaxRounds;
    private readonly int _pwMaxRounds;
    private readonly string _diagnosticLevel;
    private readonly ILogger? _logger;

    /// <summary>Accumulates classification diagnostics per turn for verbose-level runs.</summary>
    private readonly List<object> _classificationDiagnostics = [];

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Forbidden details to detect (Caleb's unique traits).</summary>
    private static readonly IReadOnlyList<string> s_forbiddenDetails =
    [
        "prosthetic arm",
        "prosthetic",
        "Toring Chip",
        "Toring",
        "custom prosthetic",
    ];

    public StrictRoleplaySessionRunner(
        ICompletionService completionService,
        DriftDetector driftDetector,
        string provider,
        string model,
        int ndMaxRounds = 8,
        int librarianMaxRounds = 1,
        int pwMaxRounds = 10,
        string diagnosticLevel = "normal",
        ILogger? logger = null)
    {
        _completionService = completionService ?? throw new ArgumentNullException(nameof(completionService));
        _driftDetector = driftDetector ?? throw new ArgumentNullException(nameof(driftDetector));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _ndMaxRounds = ndMaxRounds;
        _librarianMaxRounds = librarianMaxRounds;
        _pwMaxRounds = pwMaxRounds;
        _diagnosticLevel = diagnosticLevel;
        _logger = logger;
    }

    /// <summary>
    /// Check basic provider connectivity by sending a minimal completion request.
    /// </summary>
    public bool CanReachProvider()
    {
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = _completionService.CompleteAsync(
                new CompletionRequest
                {
                    Model = _model,
                    MaxTokens = 10,
                    Messages = [new CompletionMessage("user", new MessageContent("ping"))],
                },
                cts.Token);
            task.Wait(TimeSpan.FromSeconds(15));
            return task.IsCompletedSuccessfully;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run the strict live roleplay session lore consistency test.
    /// Drives the real NarrativeDirectorAgent pipeline with probe questions.
    /// </summary>
    public async Task<DriftHarnessRun> RunAsync(string outputDir, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var traceEvents = new List<TraceEvent>();
        var pipelineErrors = new List<PipelineError>();
        var turnIndex = 0;

        Directory.CreateDirectory(outputDir);

        Console.WriteLine("=== Strict Roleplay Session Runner ===");
        Console.WriteLine($"  Provider: {_provider}");
        Console.WriteLine($"  Model: {_model}");
        Console.WriteLine($"  ND max rounds: {_ndMaxRounds}");
        Console.WriteLine($"  Librarian max rounds: {_librarianMaxRounds}");
        Console.WriteLine($"  PW max rounds: {_pwMaxRounds}");
        Console.WriteLine($"  Diagnostic level: {_diagnosticLevel}");
        Console.WriteLine();

        // ── Build the real agent pipeline ──
        Console.WriteLine("Building real agent pipeline...");
        var pipeline = BuildPipeline(traceEvents);

        // Convert probe turns to ScriptedTurns for the run
        var probeTurns = LiveXavierCalebScenario.ProbeTurns;
        var scriptedTurns = probeTurns.Select(t => new ScriptedTurn
        {
            TurnNumber = t.TurnNumber,
            UserMessage = t.UserMessage,
            Boundaries = [],
        }).ToList();

        // ── Run each probe turn through the real NarrativeDirectorAgent ──
        foreach (var turn in probeTurns)
        {
            turnIndex++;
            ct.ThrowIfCancellationRequested();

            Console.WriteLine($"\n=== Strict Probe Turn {turn.TurnNumber}: {turn.Category} ===");
            Console.WriteLine($"User: {turn.UserMessage}");
            Console.WriteLine($"Expected subject: {turn.ExpectedSubject}");

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

            // Run through the real NarrativeDirectorAgent
            await RunNarrativeDirectorTurn(pipeline, turn, traceEvents, pipelineErrors, turnIndex, outputDir, ct);
        }

        // ── Run drift detection ──
        var completedAt = DateTimeOffset.UtcNow;
        var driftResult = _driftDetector.Detect(traceEvents, s_forbiddenDetails);

        // Build evaluation — pipeline errors override drift-based pass verdict
        var hasPipelineErrors = pipelineErrors.Count > 0;
        var passed = !driftResult.HasDrift && !hasPipelineErrors;

        var origins = driftResult.Findings
            .GroupBy(f => f.LikelyOrigin)
            .ToDictionary(g => g.Key, g => g.Count());

        var notes = hasPipelineErrors
            ? $"STRICT LIVE RUN INVALID: {pipelineErrors.Count} pipeline/provider error(s) encountered. " +
              $"The agent pipeline could not complete all turns. " +
              $"First error: [{pipelineErrors[0].Component}] {pipelineErrors[0].ErrorType}: {pipelineErrors[0].ErrorMessage}. " +
              $"Drift findings ({driftResult.Findings.Count}) are unreliable because the pipeline did not run successfully."
            : driftResult.HasDrift
                ? $"Lore bleed detected: {driftResult.Findings.Count} forbidden fact(s) appeared in the real NarrativeDirectorAgent pipeline. " +
                  $"First contaminated boundary: {driftResult.Findings[0].FirstAppearanceBoundary}. " +
                  $"Likely origin: {driftResult.Findings[0].LikelyOrigin}."
                : "No lore bleed detected through the real NarrativeDirectorAgent pipeline.";

        var evaluation = new DriftRunEvaluation
        {
            Passed = passed,
            TotalTurns = probeTurns.Count,
            TotalEvents = traceEvents.Count,
            DriftCount = driftResult.Findings.Count,
            ExpectedDriftCount = null,
            Origins = origins,
            Notes = notes,
            PipelineErrors = pipelineErrors.Count > 0 ? pipelineErrors : null,
        };

        var run = new DriftHarnessRun
        {
            RunId = runId,
            ScenarioName = "strict-xavier-caleb-session",
            ActiveCharacter = "Xavier",
            OffCharacter = "Caleb",
            Turns = scriptedTurns,
            ForbiddenDetails = s_forbiddenDetails.ToList(),
            StartedAt = startedAt,
            CompletedAt = completedAt,
            TraceEvents = traceEvents,
            DriftResult = driftResult,
            Evaluation = evaluation,
        };

        // Write artifacts
        var writer = new DriftReportWriter();
        writer.WriteAll(outputDir, run);

        // Write additional strict-mode-specific diagnostics
        WriteStrictDiagnostics(outputDir, run);

        // Write aggregated classification diagnostics (all turns) for verbose level.
        // Accumulated by CaptureLoreDiagnostics across all probe turns and written
        // once here to avoid per-turn File.WriteAllText overwrites that lose data.
        if (_diagnosticLevel == "verbose" && _classificationDiagnostics.Count > 0)
        {
            var diagPath = Path.Combine(outputDir, "classification-diagnostics.json");
            var aggregatePayload = new { turns = _classificationDiagnostics };
            File.WriteAllText(diagPath, JsonSerializer.Serialize(aggregatePayload, s_jsonOptions));
            Console.WriteLine($"  [verbose] Wrote aggregated classification diagnostics ({_classificationDiagnostics.Count} turns) to: {diagPath}");
        }

        Console.WriteLine($"\nStrict roleplay session test complete. Run ID: {run.RunId}");
        Console.WriteLine($"  Passed (no drift, no pipeline errors): {evaluation.Passed}");
        Console.WriteLine($"  Events: {traceEvents.Count}");
        Console.WriteLine($"  Drift findings: {driftResult.Findings.Count}");
        Console.WriteLine($"  Pipeline errors: {pipelineErrors.Count}");

        if (pipelineErrors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  PIPELINE ERRORS:");
            foreach (var err in pipelineErrors)
            {
                Console.WriteLine($"  - Turn {err.Turn} [{err.Component}] {err.ErrorType}: {err.ErrorMessage}");
            }
        }

        foreach (var finding in driftResult.Findings)
        {
            Console.WriteLine($"  - [{finding.LikelyOrigin}] '{finding.ForbiddenFact}' at turn {finding.FirstAppearanceTurn} in {finding.FirstAppearanceBoundary}/{finding.FirstAppearanceComponent}");
        }

        Console.WriteLine($"\nArtifacts written to: {outputDir}");

        return run;
    }

    private sealed record PipelineComponents(
        NarrativeDirectorAgent Director,
        ToolLoop ToolLoop,
        LibrarianAgent Librarian,
        InteractiveSessionContext SessionContext);

    private PipelineComponents BuildPipeline(List<TraceEvent> traceEvents)
    {
        // ── AppConfig with agent budgets ──
        var appConfig = new AppConfig
        {
            Diagnostics = new DiagnosticsConfig { LivePanel = false },
            Lore = new LoreConfig { Active = "xavier-caleb" },
            NarrativeRules = new NarrativeRulesConfig { Active = "default" },
            WritingStyle = new WritingStyleConfig { Active = "default" },
            Models = new ModelsConfig
            {
                Orchestrator = _model,
                NarrativeDirector = _model,
                ProseWriter = _model,
                Librarian = _model,
            },
            Agents = new AgentsConfig
            {
                NarrativeDirector = new NarrativeDirectorBudget
                {
                    MaxTokens = 4096,
                    MaxToolRounds = _ndMaxRounds,
                },
                Librarian = new LibrarianBudget
                {
                    MaxTokens = 4096,
                    MaxToolRounds = _librarianMaxRounds,
                    CacheSystemPrompt = true,
                },
                ProseWriter = new ProseWriterBudget
                {
                    MaxTokens = 8192,
                    MaxToolRounds = _pwMaxRounds,
                },
            },
            Timeouts = new TimeoutsConfig
            {
                ToolExecutionSeconds = 120,
                DirectSceneTimeoutSeconds = 300,
                ProviderHttpSeconds = 30,
                CompletionTimeoutSeconds = 300,
            },
        };

        // ── In-memory stores ──
        var loreStore = new InMemoryLoreStore();
        var rulesStore = new InMemoryNarrativeRulesStore();
        var writingStyleStore = new InMemoryWritingStyleStore();
        var promptStore = new InMemoryLibrarianPromptStore();
        var contentFileService = new InMemoryContentFileService();
        var assistantPromptStore = new InMemoryAssistantPromptStore();
        var storyStateService = new InMemoryStoryStateService();
        var sessionStateService = new InMemorySessionStateService();

        // Seed fixture lore
        var xavierLoreText = string.Join("\n", LiveXavierCalebScenario.XavierLore);
        var calebLoreText = string.Join("\n", LiveXavierCalebScenario.CalebLore);
        var sharedBodyTechText = string.Join("\n", LiveXavierCalebScenario.SharedBodyTech);

        loreStore.Set("xavier-caleb", new Dictionary<string, string>
        {
            ["characters/xavier.md"] = xavierLoreText,
            ["characters/caleb.md"] = calebLoreText,
            ["world/body-tech.md"] = sharedBodyTechText,
        });

        rulesStore.Set("default",
            "Write in third person past tense.\n" +
            "Re-ground against canon when characterization or scene facts are corrected.\n" +
            "Keep scene continuity tight.\n" +
            "Do not attribute off-character personal details to the active character.\n");

        writingStyleStore.Set("default",
            "Compelling narrative prose with controlled sentence rhythm and clear sensory detail.\n");

        promptStore.Set("default",
            "Treat lore as canonical source material. Prefer direct matches over genre inference.\n");

        assistantPromptStore.Set("default",
            "Be concise and accurate when summarizing tool-owned workflows.\n");

        // ── Real LLM completion chain ──
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var toolLoopLogger = loggerFactory.CreateLogger<ToolLoop>();
        var continuationStrategy = new ContinuationStrategy(loggerFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(_completionService, continuationStrategy, toolLoopLogger, appConfig);

        var canonGuard = new CanonPrerequisiteGuard(
            loreStore,
            contentFileService,
            rulesStore,
            writingStyleStore,
            loggerFactory.CreateLogger<CanonPrerequisiteGuard>());

        var librarian = new LibrarianAgent(
            toolLoop,
            loreStore,
            promptStore,
            appConfig,
            loggerFactory.CreateLogger<LibrarianAgent>());

        var queryLore = new QueryLoreHandler(
            librarian,
            loreStore,
            contentFileService,
            canonGuard,
            loggerFactory.CreateLogger<QueryLoreHandler>());

        var sessionContext = BuildSessionContext();
        var inMemSessionCtxSvc = new InMemoryInteractiveSessionContextService(sessionContext);

        var queryContext = new QueryContextHandler(
            inMemSessionCtxSvc,
            loreStore,
            loggerFactory.CreateLogger<QueryContextHandler>());

        var proseWriter = new ProseWriterAgent(
            toolLoop,
            queryLore,
            canonGuard,
            appConfig,
            loggerFactory.CreateLogger<ProseWriterAgent>());

        var getStoryState = new GetStoryStateHandler(
            storyStateService,
            inMemSessionCtxSvc,
            loggerFactory.CreateLogger<GetStoryStateHandler>());

        var updateStoryState = new UpdateStoryStateHandler(
            storyStateService,
            inMemSessionCtxSvc,
            loggerFactory.CreateLogger<UpdateStoryStateHandler>());

        var updateNarrativeState = new UpdateNarrativeStateHandler(
            sessionStateService,
            loggerFactory.CreateLogger<UpdateNarrativeStateHandler>());

        var writeProse = new WriteProseHandler(
            proseWriter,
            inMemSessionCtxSvc,
            storyStateService,
            loggerFactory.CreateLogger<WriteProseHandler>());

        var director = new NarrativeDirectorAgent(
            toolLoop,
            queryLore,
            updateStoryState,
            updateNarrativeState,
            writeProse,
            canonGuard,
            rulesStore,
            appConfig,
            loggerFactory.CreateLogger<NarrativeDirectorAgent>(),
            queryContext);

        return new PipelineComponents(director, toolLoop, librarian, sessionContext);
    }

    private async Task RunNarrativeDirectorTurn(
        PipelineComponents pipeline,
        LiveProbeTurn turn,
        List<TraceEvent> traceEvents,
        List<PipelineError> pipelineErrors,
        int turnIndex,
        string outputDir,
        CancellationToken ct)
    {
        try
        {
            // Create AgentContext with Xavier session context
            var sessionId = Guid.NewGuid();
            var agentContext = new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "xavier-caleb",
                ActiveWritingStyle = "default",
                ActiveNarrativeRules = "default",
                LibrarianPrompt = "default",
                SessionContext = pipeline.SessionContext,
            };

            var turnStartedAt = DateTimeOffset.UtcNow;

            // ── Drive the real NarrativeDirectorAgent ──
            // NOTE: Only turn.UserMessage is passed to DirectSceneAsync.
            // turn.ProbePrompt is intentionally NOT injected here. The probe
            // prompts are documentation-only test oracle descriptions — they
            // describe the lore boundaries the probe expects the unaltered
            // pipeline to respect on its own. Injecting them as system prompts
            // or additional instructions would change the behavior under test.
            // See LiveProbeTurn.ProbePrompt for full rationale.
            Console.WriteLine("  [NarrativeDirectorAgent.DirectSceneAsync]");
            var ndStopwatch = Stopwatch.StartNew();

            var result = await pipeline.Director.DirectSceneAsync(
                new NarrativeDirectionRequest
                {
                    UserMessage = turn.UserMessage,
                },
                agentContext,
                ct);

            ndStopwatch.Stop();

            // ── Capture Narrative Director boundary ──
            var ndResponse = result.ResponseText ?? "";
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "narrative_director",
                Boundary = nameof(BoundaryType.NarrativeDirector),
                Agent = "NarrativeDirectorAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = turnStartedAt,
                DurationMs = ndStopwatch.ElapsedMilliseconds,
                Preview = Truncate(ndResponse, 200),
                Content = ndResponse,
                SourceRefs = [],
            });

            Console.WriteLine($"  Response ({ndStopwatch.ElapsedMilliseconds}ms): {Truncate(ndResponse, 100)}");

            // ── Capture ProseWriter boundary (the final prose) ──
            // ACKNOWLEDGED APPROXIMATION: The Director's response IS the prose
            // (delegated to WriteProseHandler internally). This trace event does
            // NOT independently capture the real WriteProseHandler output because
            // the strict harness drives the NarrativeDirectorAgent at the top level
            // and does not have a separate separation point to intercept the prose
            // writer's intermediate result.
            //
            // What this means:
            //   - Content/Preview: duplicates the NarrativeDirector response text.
            //   - DurationMs: estimated as half of ND's total wall time. This is
            //     a rough heuristic — the actual prose writer sub-duration depends
            //     on internal Director delegation timing which varies per run.
            //
            // To get true ProseWriter timing and output, the harness would need to
            // instrument WriteProseHandler directly (e.g., via a wrapped handler
            // that emits a callback event). That is deferred as out-of-scope for
            // the initial #1675 harness. See NarrativeDirectorAgent -> WriteProseHandler
            // delegation chain in src/QuillForge.Core/Agents/.
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "prose_writer",
                Boundary = nameof(BoundaryType.ProseWriter),
                Agent = "ProseWriterAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = turnStartedAt.AddMilliseconds(ndStopwatch.ElapsedMilliseconds / 2),
                DurationMs = ndStopwatch.ElapsedMilliseconds / 2,
                Preview = Truncate(ndResponse, 200),
                Content = ndResponse,
                SourceRefs = [],
            });

            // ── Capture Visible Response boundary ──
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "visible_response",
                Boundary = nameof(BoundaryType.VisibleResponse),
                Provider = _provider,
                Model = _model,
                Timestamp = turnStartedAt.AddMilliseconds(ndStopwatch.ElapsedMilliseconds),
                DurationMs = ndStopwatch.ElapsedMilliseconds,
                Preview = Truncate(ndResponse, 200),
                Content = ndResponse,
                SourceRefs = [],
            });

            // ── Add diagnostics from the real pipeline ──
            if (_diagnosticLevel != "minimal")
            {
                await CaptureLoreDiagnostics(pipeline, turn, traceEvents, turnIndex, outputDir, ct);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  [TIMEOUT] Narrative Director turn was cancelled or timed out.");
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "narrative_director",
                Boundary = nameof(BoundaryType.NarrativeDirector),
                Agent = "NarrativeDirectorAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = DateTimeOffset.UtcNow,
                Preview = "Narrative Director timed out",
                Content = "Narrative Director timed out or was cancelled during this probe turn.",
            });
            pipelineErrors.Add(new PipelineError
            {
                Turn = turn.TurnNumber,
                Component = "narrative_director",
                ErrorType = "Timeout",
                ErrorMessage = "Narrative Director Agent was cancelled or timed out during DirectSceneAsync.",
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [ERROR] Narrative Director failed: {ex.GetType().Name}: {ex.Message}");
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "narrative_director",
                Boundary = nameof(BoundaryType.NarrativeDirector),
                Agent = "NarrativeDirectorAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = DateTimeOffset.UtcNow,
                Preview = $"Error: {Truncate(ex.Message, 200)}",
                Content = $"ERROR: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}",
            });
            pipelineErrors.Add(new PipelineError
            {
                Turn = turn.TurnNumber,
                Component = "narrative_director",
                ErrorType = ex.GetType().Name,
                ErrorMessage = $"{ex.GetType().FullName}: {ex.Message}",
            });
        }
    }

    private async Task CaptureLoreDiagnostics(
        PipelineComponents pipeline,
        LiveProbeTurn turn,
        List<TraceEvent> traceEvents,
        int turnIndex,
        string outputDir,
        CancellationToken ct)
    {
        var agentContext = new AgentContext
        {
            SessionId = Guid.NewGuid(),
            ActiveMode = Mode.Roleplay,
            ActiveLoreSet = "xavier-caleb",
            ActiveWritingStyle = "default",
            ActiveNarrativeRules = "default",
            LibrarianPrompt = "default",
            SessionContext = pipeline.SessionContext,
        };

        var query = $"Tell me about {turn.ExpectedSubject}'s appearance, gear, and distinctive features";
        Console.WriteLine($"  [LibrarianAgent.QueryAsync] \"{query}\"");

        try
        {
            var loreResult = await pipeline.Librarian.QueryAsync(
                query,
                "xavier-caleb",
                agentContext,
                supplementalLore: null,
                ct);

            var bundle = loreResult.Bundle;

            // Record the retrieval provenance
            traceEvents.Add(new TraceEvent
            {
                Turn = turn.TurnNumber,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Agent = "LibrarianAgent",
                Provider = _provider,
                Model = _model,
                Timestamp = DateTimeOffset.UtcNow,
                Preview = Truncate(string.Join("; ", bundle.RelevantPassages.Take(3)), 300),
                Content = string.Join("\n---\n", bundle.RelevantPassages),
                SourceRefs = bundle.SourceFiles.ToList(),
                StructuredPayload = new StructuredPayload
                {
                    ActiveSubject = turn.ExpectedSubject,
                    Applicability = bundle.Confidence.ToString(),
                    AllowedUse = "AssertAsFact",
                    LoreRefs = bundle.SourceFiles.ToList(),
                    SourceComponent = "librarian",
                },
            });

            Console.WriteLine($"  Retrieved {bundle.RelevantPassages.Count} passages from {bundle.SourceFiles.Count} sources (confidence: {bundle.Confidence})");
            foreach (var src in bundle.SourceFiles)
            {
                Console.WriteLine($"    Source: {src}");
            }

            // Capture the diagnostic classification if available
            var offCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };
            if (bundle.RelevantPassages.Count > 0)
            {
                var allDiagnostics = new List<ClassificationDiagnostic>();

                foreach (var (passage, i) in bundle.RelevantPassages.Select((p, i) => (p, i)))
                {
                    var sourcePath = i < bundle.SourceFiles.Count ? bundle.SourceFiles[i] : null;
                    var diagnostic = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                        passage,
                        turn.ExpectedSubject,
                        sourcePath,
                        offCharacterNames,
                        offCharacterNames);

                    allDiagnostics.Add(diagnostic);

                    if (_diagnosticLevel == "verbose")
                    {
                        Console.WriteLine($"    Classification: {diagnostic.Applicability}/{diagnostic.AllowedUse}");
                        Console.WriteLine($"    Rules fired: {string.Join(", ", diagnostic.RulesFired ?? [])}");
                    }
                }

                // Accumulate classification diagnostics for offline auditability at verbose level.
                // Appended per turn and written once as an aggregate after all turns complete,
                // so no turn's diagnostic data is lost to overwrites.
                if (_diagnosticLevel == "verbose" && allDiagnostics.Count > 0)
                {
                    _classificationDiagnostics.Add(new
                    {
                        turn = turn.TurnNumber,
                        category = turn.Category,
                        expected_subject = turn.ExpectedSubject,
                        diagnostics = allDiagnostics.Select(d => new
                        {
                            passage = d.Passage,
                            active_subject = d.ActiveSubject,
                            source_path = d.SourcePath,
                            applicability = d.Applicability.ToString(),
                            allowed_use = d.AllowedUse.ToString(),
                            scope = d.Scope.ToString(),
                            source_kind = d.SourceKind.ToString(),
                            authority = d.Authority.ToString(),
                            rules_fired = d.RulesFired,
                        }),
                    });
                    Console.WriteLine($"  [verbose] Accumulated classification diagnostics for turn {turn.TurnNumber} ({_classificationDiagnostics.Count} turns total).");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WARN] Librarian diagnostic query failed: {ex.Message}");
        }
    }

    private static InteractiveSessionContext BuildSessionContext()
    {
        return new InteractiveSessionContext
        {
            ActiveMode = Mode.Roleplay,
            ProjectName = "strict-test-1675",
            StoryStatePath = "strict-test-1675/.state.yaml",
            CurrentFile = "strict-test-1675/scene.md",
            Character = "Xavier",
            CharacterSection =
                "## Character: Xavier\n\n" +
                "Xavier is a Deepspace Hunter in the Division.\n" +
                "He has silver-streaked black hair and sharp grey eyes.\n" +
                "He carries a standard-issue hunter carbine and combat knife.\n" +
                "He has standard Division neural interface augmentation.\n" +
                "He has a faint scar above his left eyebrow from a Deepspace patrol.\n" +
                "Xavier's equipment is standard Division-issue — well-worn and practical.\n",
            UserCharacter = "Aurora",
            UserCharacterSection =
                "## User Character: Aurora\n\n" +
                "Aurora is a researcher assigned to the Deepspace Division.\n" +
                "She is perceptive and analytically minded.\n",
            StickySessionCanon = "- Xavier and Aurora are on a routine Deepspace patrol together.\n" +
                                 "- Xavier is a standard Division operative with standard-issue gear.\n" +
                                 "- Caleb is a separate Division operative with custom equipment.\n",
            DirectorNotes = "This is the first turn. Establish Xavier's appearance and demeanor naturally.",
            ActivePlotContent = "",
            PlotProgressSummary = "",
            RecentConversationSummary = "",
        };
    }

    private static void WriteStrictDiagnostics(string outputDir, DriftHarnessRun run)
    {
        // Write provider metadata
        var metaPath = Path.Combine(outputDir, "provider-meta.json");
        var meta = new
        {
            provider = "StrictRoleplaySessionRunner",
            model = "(resolved per-agent via ToolLoop)",
            narrative_director_type = "NarrativeDirectorAgent",
            prose_writer_type = "ProseWriterAgent",
            librarian_type = "LibrarianAgent",
            tool_loop_type = "ToolLoop",
        };

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, s_jsonOptions));

        // Write session context snapshot
        var sessionPath = Path.Combine(outputDir, "session-context.json");
        var session = new
        {
            active_character = "Xavier",
            off_character = "Caleb",
            lore_set = "xavier-caleb",
            forbidden_details = s_forbiddenDetails,
        };

        File.WriteAllText(sessionPath, JsonSerializer.Serialize(session, s_jsonOptions));

        // Write drift origin analysis
        if (run.DriftResult.HasDrift)
        {
            var driftPath = Path.Combine(outputDir, "drift-origin-analysis.json");
            var analysis = new
            {
                drift_count = run.DriftResult.Findings.Count,
                first_contaminated_boundary = run.DriftResult.Findings[0].FirstAppearanceBoundary,
                origins = run.DriftResult.Findings
                    .GroupBy(f => f.LikelyOrigin)
                    .ToDictionary(g => g.Key, g => g.Count()),
                findings = run.DriftResult.Findings.Select(f => new
                {
                    forbidden_fact = f.ForbiddenFact,
                    first_appearance_turn = f.FirstAppearanceTurn,
                    first_appearance_boundary = f.FirstAppearanceBoundary,
                    first_appearance_component = f.FirstAppearanceComponent,
                    likely_origin = f.LikelyOrigin,
                    evidence = f.Evidence,
                }),
            };

            File.WriteAllText(driftPath, JsonSerializer.Serialize(analysis, s_jsonOptions));
        }
    }

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }

    // ──────────────────────────────────────────────
    // In-Memory Store Implementations
    // ──────────────────────────────────────────────

    private sealed class InMemoryLoreStore : ILoreStore
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data = new();

        public void Set(string loreSetName, Dictionary<string, string> content)
        {
            _data[loreSetName] = content;
        }

        public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
        {
            if (_data.TryGetValue(loreSetName, out var content))
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>(content));
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());
        }

        public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_data.Keys.ToList());
        }

        public async Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
            string loreSetName, string query, CancellationToken ct = default)
        {
            var content = await LoadLoreSetAsync(loreSetName, ct);
            var results = new List<(string, string)>();
            foreach (var (path, text) in content)
            {
                if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var snippet = text.Length > 200 ? text[..200] + "…" : text;
                    results.Add((path, snippet));
                }
            }
            return results;
        }
    }

    private sealed class InMemoryNarrativeRulesStore : INarrativeRulesStore
    {
        private string _content = "";
        private string _name = "";

        public void Set(string name, string content)
        {
            _name = name;
            _content = content;
        }

        public Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
        {
            return Task.FromResult(rulesName == _name ? _content : "");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(string.IsNullOrEmpty(_name) ? [] : [_name]);
        }
    }

    private sealed class InMemoryWritingStyleStore : IWritingStyleStore
    {
        private string _content = "";
        private string _name = "";

        public void Set(string name, string content)
        {
            _name = name;
            _content = content;
        }

        public Task<string> LoadAsync(string styleName, CancellationToken ct = default)
        {
            return Task.FromResult(styleName == _name ? _content : "");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(string.IsNullOrEmpty(_name) ? [] : [_name]);
        }
    }

    private sealed class InMemoryLibrarianPromptStore : ILibrarianPromptStore
    {
        private string _content = "";
        private string _name = "";

        public void Set(string name, string content)
        {
            _name = name;
            _content = content;
        }

        public Task<string> LoadAsync(string promptName, CancellationToken ct = default)
        {
            return Task.FromResult(promptName == _name ? _content : "");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(string.IsNullOrEmpty(_name) ? [] : [_name]);
        }
    }

    private sealed class InMemoryAssistantPromptStore : IAssistantPromptStore
    {
        private string _content = "";
        private string _name = "";

        public void Set(string name, string content)
        {
            _name = name;
            _content = content;
        }

        public Task<string> LoadAsync(string promptName, CancellationToken ct = default)
        {
            return Task.FromResult(promptName == _name ? _content : "");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(string.IsNullOrEmpty(_name) ? [] : [_name]);
        }
    }

    private sealed class InMemoryContentFileService : IContentFileService
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
        {
            return _files.TryGetValue(relativePath, out var content)
                ? Task.FromResult(content)
                : throw new FileNotFoundException($"File not found: {relativePath}", relativePath);
        }

        public Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
        {
            _files[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
        {
            var results = _files.Keys
                .Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(results);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        {
            return Task.FromResult(_files.ContainsKey(relativePath));
        }

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            _files.Remove(relativePath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
            string directory, string query, CancellationToken ct = default)
        {
            var results = new List<(string, string)>();
            foreach (var (path, content) in _files)
            {
                if (path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                    content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var snippet = content.Length > 200 ? content[..200] + "…" : content;
                    results.Add((path, snippet));
                }
            }
            return Task.FromResult<IReadOnlyList<(string FilePath, string Snippet)>>(results);
        }
    }

    private sealed class InMemoryInteractiveSessionContextService : IInteractiveSessionContextService
    {
        private readonly InteractiveSessionContext _context;

        public InMemoryInteractiveSessionContextService(InteractiveSessionContext context)
        {
            _context = context;
        }

        public Task<InteractiveSessionContext> BuildAsync(SessionState state, CancellationToken ct = default)
        {
            return Task.FromResult(_context);
        }

        public Task<InteractiveSessionContext> LoadAsync(Guid? sessionId, CancellationToken ct = default)
        {
            return Task.FromResult(_context);
        }
    }

    private sealed class InMemoryStoryStateService : IStoryStateService
    {
        private readonly Dictionary<string, Dictionary<string, object>> _states = new();

        public Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default)
        {
            if (_states.TryGetValue(stateFilePath, out var state))
                return Task.FromResult<IReadOnlyDictionary<string, object>>(
                    new Dictionary<string, object>(state));
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }

        public Task SaveAsync(string stateFilePath, IReadOnlyDictionary<string, object> state, CancellationToken ct = default)
        {
            _states[stateFilePath] = new Dictionary<string, object>(state);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> MergeAsync(
            string stateFilePath, IReadOnlyDictionary<string, object> updates, CancellationToken ct = default)
        {
            if (!_states.TryGetValue(stateFilePath, out var existing))
                existing = new Dictionary<string, object>();
            foreach (var (key, value) in updates)
                existing[key] = value;
            _states[stateFilePath] = existing;
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>(existing));
        }

        public Task IncrementCounterAsync(string stateFilePath, string counterKey, CancellationToken ct = default)
        {
            if (!_states.TryGetValue(stateFilePath, out var state))
                state = new Dictionary<string, object>();
            if (state.TryGetValue(counterKey, out var val) && val is int i)
                state[counterKey] = i + 1;
            else
                state[counterKey] = 1;
            _states[stateFilePath] = state;
            return Task.CompletedTask;
        }

        public Task RemoveKeyAsync(string stateFilePath, string key, CancellationToken ct = default)
        {
            if (_states.TryGetValue(stateFilePath, out var state))
                state.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySessionStateService : ISessionStateService
    {
        private readonly SessionState _state = new()
        {
            SessionId = Guid.NewGuid(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "strict-test-1675",
                Character = "Xavier",
            },
            Profile = new ProfileState
            {
                ActiveLoreSet = "xavier-caleb",
                ActiveNarrativeRules = "default",
                ActiveWritingStyle = "default",
                ActiveLibrarianPrompt = "default",
            },
            Narrative = new NarrativeRuntimeState(),
        };

        public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
        {
            return Task.FromResult(_state);
        }

        public Task<SessionMutationResult<SessionState>> SetProfileAsync(
            Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
        {
            if (command.LoreSet is not null) _state.Profile.ActiveLoreSet = command.LoreSet;
            if (command.NarrativeRules is not null) _state.Profile.ActiveNarrativeRules = command.NarrativeRules;
            if (command.WritingStyle is not null) _state.Profile.ActiveWritingStyle = command.WritingStyle;
            if (command.LibrarianPrompt is not null) _state.Profile.ActiveLibrarianPrompt = command.LibrarianPrompt;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }

        public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(
            Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
        {
            if (command.AiCharacter is not null)
                _state.Roleplay.ActiveAiCharacter = command.AiCharacter;
            if (command.UserCharacter is not null)
                _state.Roleplay.ActiveUserCharacter = command.UserCharacter;
            _state.Roleplay.HasExplicitAiCharacterSelection = command.HasAiCharacterSelection;
            _state.Roleplay.HasExplicitUserCharacterSelection = command.HasUserCharacterSelection;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }

        public Task<SessionMutationResult<SessionState>> SetModeAsync(
            Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
        {
            if (command.Mode is not null)
                _state.Mode.ActiveMode = ModeExtensions.TryParseMode(command.Mode) ?? Mode.Roleplay;
            if (command.Project is not null) _state.Mode.ProjectName = command.Project;
            if (command.File is not null) _state.Mode.CurrentFile = command.File;
            if (command.Character is not null) _state.Mode.Character = command.Character;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }

        public Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(
            Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(SessionMutationResult<WriterPendingCaptureEvent>.Success(
                new WriterPendingContentCapturedEvent(_state, command.Content.Length, command.SourceMode)));
        }

        public Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(
            Guid? sessionId, CancellationToken ct = default)
        {
            return Task.FromResult(SessionMutationResult<WriterPendingContentAcceptedEvent>.Success(
                new WriterPendingContentAcceptedEvent(_state.SessionId, "", "")));
        }

        public Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(
            Guid? sessionId, CancellationToken ct = default)
        {
            return Task.FromResult(SessionMutationResult<WriterPendingContentRejectedEvent>.Success(
                new WriterPendingContentRejectedEvent(_state)));
        }

        public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(
            Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
        {
            _state.Narrative.DirectorNotes = command.DirectorNotes;
            _state.Narrative.StickySessionCanon = command.StickySessionCanon;
            if (command.ActivePlotFile is not null)
                _state.Narrative.ActivePlotFile = command.ActivePlotFile;
            _state.LastModified = DateTimeOffset.UtcNow;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }

        public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(
            Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
        {
            _state.Narrative.ActivePlotFile = command.PlotFileName;
            _state.LastModified = DateTimeOffset.UtcNow;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }

        public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(
            Guid? sessionId, CancellationToken ct = default)
        {
            _state.Narrative.ActivePlotFile = null;
            _state.LastModified = DateTimeOffset.UtcNow;
            return Task.FromResult(SessionMutationResult<SessionState>.Success(_state));
        }
    }
}
