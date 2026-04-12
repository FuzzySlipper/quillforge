using System.Text.Json;
using QuillForge.Core;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessForgeScenarioTests
{
    [Fact]
    public async Task CanonicalForgePauseResumeScenario_RunsEndToEndAgainstHarnessProvider()
    {
        var projectName = "moon-heist";
        var premise = "A jewel thief is forced into an arranged marriage during the winter gala.";

        var scenario = new HarnessProviderScenario
        {
            Name = "forge-pause-resume",
            Models =
            [
                "forge-planner-model",
                "forge-writer-model",
                "forge-reviewer-model",
                "forge-librarian-model",
            ],
            Responses =
            [
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-planner-model",
                    Message = new HarnessAssistantMessage
                    {
                        ToolCalls =
                        [
                            new HarnessToolCallPlan(
                                "plan_premise",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/premise.md",
                                    content = premise,
                                })),
                            new HarnessToolCallPlan(
                                "plan_outline",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/outline.md",
                                    content = "# Outline\n\n## ch-01\nAurora and Lucian survive the winter gala by presenting a united front.",
                                })),
                            new HarnessToolCallPlan(
                                "plan_style",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/style.md",
                                    content = "# Style\nThird-person intimate romantic suspense with elegant atmosphere.",
                                })),
                            new HarnessToolCallPlan(
                                "plan_bible",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/bible.md",
                                    content = "# Bible\nAurora and Lucian are bound by contract and tested at the winter gala.",
                                })),
                            new HarnessToolCallPlan(
                                "plan_brief",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/ch-01-brief.md",
                                    content = "# ch-01 brief\nTarget word count: 500\nPlot beats: Aurora must appear composed, Lucian must help her keep the sapphire ring hidden, and they must leave the gala aligned.",
                                })),
                        ],
                    },
                    Usage = new HarnessUsage(140, 55),
                    FinishReason = "tool_calls",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-planner-model",
                    Message = new HarnessAssistantMessage
                    {
                        Content = "Planning complete.",
                    },
                    Usage = new HarnessUsage(60, 12),
                    FinishReason = "stop",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-planner-model",
                    Message = new HarnessAssistantMessage
                    {
                        ToolCalls =
                        [
                            new HarnessToolCallPlan(
                                "design_brief",
                                "write_file",
                                SerializeArgs(new
                                {
                                    directory = "forge",
                                    path = $"{projectName}/plan/ch-01-brief.md",
                                    content = "# ch-01 brief\nTarget word count: 500\nPlot beats: Aurora must appear composed, Lucian quietly shields her, the sapphire ring stays hidden in the conservatory wall, and they leave the winter gala presenting a believable alliance.",
                                })),
                        ],
                    },
                    Usage = new HarnessUsage(110, 40),
                    FinishReason = "tool_calls",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-planner-model",
                    Message = new HarnessAssistantMessage
                    {
                        Content = "Design refinement complete.",
                    },
                    Usage = new HarnessUsage(45, 10),
                    FinishReason = "stop",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-writer-model",
                    Message = new HarnessAssistantMessage
                    {
                        ToolCalls =
                        [
                            new HarnessToolCallPlan(
                                "writer_lore",
                                "query_lore",
                                """{"query":"Where is the sapphire ring hidden and what public pressure binds Aurora and Lucian?"}"""),
                        ],
                    },
                    Usage = new HarnessUsage(90, 18),
                    FinishReason = "tool_calls",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-librarian-model",
                    Message = new HarnessAssistantMessage
                    {
                        Content =
                            """
                            {
                              "relevant_passages": [
                                "Aurora once hid a sapphire ring inside the conservatory wall.",
                                "The arranged marriage contract binds Aurora and Lucian to present a united front."
                              ],
                              "source_files": [
                                "world.md",
                                "world.md"
                              ],
                              "confidence": "high"
                            }
                            """,
                    },
                    Usage = new HarnessUsage(55, 20),
                    FinishReason = "stop",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-writer-model",
                    Message = new HarnessAssistantMessage
                    {
                        Content =
                            """
                            Aurora crossed the conservatory with her shoulders perfectly level, as if the winter gala had asked nothing of her but grace. Behind the ivy lattice, the sapphire ring waited in its hidden seam, cold insurance against the contract that now bound her to Lucian in the eyes of every watching guest.

                            Lucian did not crowd her. He merely stepped into the line of sight of the nearest rivals and gave Aurora a quiet nod, an offer of cover disguised as effortless charm. Together they answered every test with composure until the music thinned and the hall finally released them.
                            """,
                    },
                    Usage = new HarnessUsage(130, 85),
                    FinishReason = "stop",
                },
                new HarnessResponsePlan
                {
                    ExpectedModel = "forge-reviewer-model",
                    Message = new HarnessAssistantMessage
                    {
                        Content =
                            """
                            {
                              "continuity": 9,
                              "brief_adherence": 9,
                              "voice_consistency": 8,
                              "quality": 8,
                              "feedback": "Strong chapter one. Keep the contract pressure visible in later scenes.",
                              "extracted_details": [
                                "Aurora keeps the sapphire ring hidden inside the conservatory wall.",
                                "Lucian shields Aurora from rival scrutiny without making a scene."
                              ]
                            }
                            """,
                    },
                    Usage = new HarnessUsage(80, 35),
                    FinishReason = "stop",
                },
            ],
        };

        await using var providerHost = await HarnessProviderHost.StartAsync(scenario);
        await using var runner = new HarnessForgeScenarioRunner(providerHost);

        var report = await runner.RunCanonicalPauseResumeScenarioAsync(projectName, premise);

        Assert.Equal("forge-pause-resume", report.ScenarioName);
        Assert.Equal(projectName, report.ProjectName);
        Assert.Contains(report.Phases, phase => phase.PhaseName == "design");
        Assert.Contains(report.Phases, phase => phase.PhaseName == "start");

        foreach (var phase in report.Phases)
        {
            Assert.True(
                phase.Evaluation.Status == HarnessEvaluationStatus.Passed,
                FormatFailures(phase));
            Assert.NotEmpty(phase.Run.ProviderTraces);
            Assert.NotNull(phase.Run.ForgeTrace);
            Assert.NotNull(phase.Run.ForgeManifest);
        }

        Assert.Contains(report.Phases, phase => phase.PhaseName == "approve");

        var startPhase = Assert.Single(report.Phases, phase => phase.PhaseName == "start");
        Assert.Contains(
            startPhase.Run.ForgeTrace!.Events,
            evt => evt is ForgePausedObserved);

        var approvePhase = Assert.Single(report.Phases, phase => phase.PhaseName == "approve");
        var outputArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/output/story.md");
        Assert.True(outputArtifact.Exists);
        Assert.Contains("Aurora crossed the conservatory", outputArtifact.Content);

        var runLoreArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/run-lore.md");
        Assert.True(runLoreArtifact.Exists);
        Assert.Contains("sapphire ring", runLoreArtifact.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFailures(HarnessForgePhaseReport phase)
    {
        if (phase.Evaluation.Findings.Count == 0)
        {
            return $"Phase '{phase.PhaseName}' failed without recorded findings.";
        }

        var lines = new List<string>
        {
            $"Phase '{phase.PhaseName}' failed.",
        };

        foreach (var finding in phase.Evaluation.Findings)
        {
            lines.Add($"[{finding.Severity}] {finding.Code}");
            lines.Add($"Expected: {finding.Expected}");
            lines.Add($"Actual: {finding.Actual}");
            lines.Add($"Evidence: {string.Join(", ", finding.Evidence)}");
        }

        if (phase.Run.ProviderTraces.Count > 0)
        {
            lines.Add("Provider requests:");
            for (var i = 0; i < phase.Run.ProviderTraces.Count; i++)
            {
                var trace = phase.Run.ProviderTraces[i];
                lines.Add($"[{i}] model={trace.Model} path={trace.Path}");
                lines.Add(trace.RawRequestBody);
            }
        }

        if (phase.Run.ForgeTrace is not null)
        {
            lines.Add("Forge events:");
            foreach (var evt in phase.Run.ForgeTrace.Events)
            {
                lines.Add(evt switch
                {
                    ForgeStageStartedObserved started => $"stage_started:{started.StageName}",
                    ForgeStageCompletedObserved completed => $"stage_completed:{completed.StageName}",
                    ForgeChapterObserved chapter => $"chapter:{chapter.ChapterId}:{chapter.ChapterStatus}:{chapter.Detail}",
                    ForgeProgressObserved progress => $"progress:{progress.Source}:{progress.Message}",
                    ForgePausedObserved paused => $"pause:{paused.Message}",
                    ForgeCompletedObserved completed => $"complete:{completed.TotalTokens}:{completed.ChaptersComplete}",
                    ForgeErrorObserved error => $"error:{error.Source}:{error.Message}",
                    ForgeUnknownObserved unknown => $"unknown:{unknown.Type}:{unknown.Message}",
                    _ => evt.GetType().Name,
                });
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SerializeArgs(object value)
    {
        return JsonSerializer.Serialize(value);
    }
}
