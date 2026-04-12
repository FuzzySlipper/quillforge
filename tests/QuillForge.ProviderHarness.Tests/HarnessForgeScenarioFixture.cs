using System.Text.Json;
using QuillForge.Core;

namespace QuillForge.ProviderHarness.Tests;

public sealed record HarnessForgeScenarioFixture
{
    public required string Name { get; init; }
    public required string ProjectName { get; init; }
    public required string Premise { get; init; }
    public required HarnessProviderScenario ProviderScenario { get; init; }
    public IReadOnlyList<HarnessForgePhaseFixture> Phases { get; init; } = [];
}

public sealed record HarnessForgePhaseFixture
{
    public required string Name { get; init; }
    public required string Operation { get; init; }
    public IReadOnlyList<string> ArtifactPaths { get; init; } = [];
    public HarnessForgePhaseExpectations Expectations { get; init; } = new();
}

public sealed record HarnessForgePhaseExpectations
{
    public IReadOnlyList<string> ProviderRequestSections { get; init; } = [];
    public string? ExpectedManifestStage { get; init; }
    public bool? ExpectedPaused { get; init; }
    public IReadOnlyList<string> ExpectedChapterIds { get; init; } = [];
    public bool RequirePauseSurfaced { get; init; }
    public bool RequireStatusMatchesManifest { get; init; } = true;
    public IReadOnlyList<string> ExpectedArtifactPaths { get; init; } = [];
}

public static class HarnessForgeScenarioFixtures
{
    public static HarnessForgeScenarioFixture CreateCanonicalPauseResume(
        string projectName,
        string premise,
        string scenarioName = "forge-pause-resume")
    {
        return new HarnessForgeScenarioFixture
        {
            Name = scenarioName,
            ProjectName = projectName,
            Premise = premise,
            ProviderScenario = CreateCanonicalProviderScenario(projectName, premise, scenarioName),
            Phases =
            [
                new HarnessForgePhaseFixture
                {
                    Name = "design",
                    Operation = "design",
                    ArtifactPaths =
                    [
                        $"{ContentPaths.Forge}/{projectName}/manifest.json",
                        $"{ContentPaths.Forge}/{projectName}/plan/premise.md",
                        $"{ContentPaths.Forge}/{projectName}/plan/outline.md",
                        $"{ContentPaths.Forge}/{projectName}/plan/style.md",
                        $"{ContentPaths.Forge}/{projectName}/plan/bible.md",
                        $"{ContentPaths.Forge}/{projectName}/plan/ch-01-brief.md",
                    ],
                    Expectations = new HarnessForgePhaseExpectations
                    {
                        ProviderRequestSections = [premise],
                        ExpectedManifestStage = "Writing",
                        ExpectedPaused = true,
                        ExpectedChapterIds = ["ch-01"],
                        ExpectedArtifactPaths =
                        [
                            $"{ContentPaths.Forge}/{projectName}/plan/outline.md",
                            $"{ContentPaths.Forge}/{projectName}/plan/style.md",
                            $"{ContentPaths.Forge}/{projectName}/plan/bible.md",
                            $"{ContentPaths.Forge}/{projectName}/plan/ch-01-brief.md",
                        ],
                    },
                },
                new HarnessForgePhaseFixture
                {
                    Name = "start",
                    Operation = "start",
                    ArtifactPaths =
                    [
                        $"{ContentPaths.Forge}/{projectName}/manifest.json",
                        $"{ContentPaths.Forge}/{projectName}/drafts/ch-01.md",
                        $"{ContentPaths.Forge}/{projectName}/run-lore.md",
                    ],
                    Expectations = new HarnessForgePhaseExpectations
                    {
                        ProviderRequestSections = ["## Chapter Brief"],
                        ExpectedManifestStage = "Review",
                        ExpectedPaused = true,
                        ExpectedChapterIds = ["ch-01"],
                        RequirePauseSurfaced = true,
                        ExpectedArtifactPaths =
                        [
                            $"{ContentPaths.Forge}/{projectName}/drafts/ch-01.md",
                        ],
                    },
                },
                new HarnessForgePhaseFixture
                {
                    Name = "approve",
                    Operation = "approve",
                    ArtifactPaths =
                    [
                        $"{ContentPaths.Forge}/{projectName}/manifest.json",
                        $"{ContentPaths.Forge}/{projectName}/output/story.md",
                        $"{ContentPaths.Forge}/{projectName}/run-lore.md",
                    ],
                    Expectations = new HarnessForgePhaseExpectations
                    {
                        ProviderRequestSections = ["## Chapter Draft"],
                        ExpectedManifestStage = "Done",
                        ExpectedPaused = false,
                        ExpectedArtifactPaths =
                        [
                            $"{ContentPaths.Forge}/{projectName}/output/story.md",
                            $"{ContentPaths.Forge}/{projectName}/run-lore.md",
                        ],
                    },
                },
            ],
        };
    }

    private static HarnessProviderScenario CreateCanonicalProviderScenario(
        string projectName,
        string premise,
        string scenarioName)
    {
        return new HarnessProviderScenario
        {
            Name = scenarioName,
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
    }

    private static string SerializeArgs(object value)
    {
        return JsonSerializer.Serialize(value);
    }
}
