using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessInteractiveCostProfileTests
{
    private static readonly string[] ExpectedModelSequence =
    [
        "orchestrator-model",
        "narrative-director-model",
        "librarian-model",
        "prose-writer-model",
        "librarian-model",
        "prose-writer-model",
        "narrative-director-model",
        "orchestrator-model",
    ];

    [Fact]
    public async Task WriterTurn_UsesExpectedLayeredCallProfile()
    {
        await using var providerHost = await HarnessProviderHost.StartAsync(
            new InteractiveCostProfileResponseSource());
        await using var runner = new HarnessInteractiveScenarioRunner(providerHost);

        var report = await runner.RunTurnAsync(
            Mode.Writer,
            "Write the moment Aurora realizes the sapphire ring was hidden in the conservatory wall.");

        Assert.Equal("writer", report.Mode);
        Assert.Equal(ExpectedModelSequence, report.Run.ProviderTraces.Select(trace => trace.Model).ToArray());
        Assert.Equal(8, report.Run.ProviderTraces.Count);
        Assert.Equal(8, report.UsageSummary.TotalRequests);
        Assert.Equal(1, report.Run.AppTrace?.ToolRounds);
        Assert.Equal("pendingreview", report.Run.AppTrace?.WriterState);
        Assert.Equal(
            new[]
            {
                "librarian:2",
                "narrative-director:2",
                "orchestrator:2",
                "prose-writer:2",
            },
            GetAgentCounts(report.UsageSummary));
    }

    [Fact]
    public async Task RoleplayTurn_UsesExpectedLayeredCallProfile()
    {
        await using var providerHost = await HarnessProviderHost.StartAsync(
            new InteractiveCostProfileResponseSource());
        await using var runner = new HarnessInteractiveScenarioRunner(providerHost);

        var report = await runner.RunTurnAsync(
            Mode.Roleplay,
            "Aurora, what did you hide in the conservatory wall?",
            character: "aurora");

        Assert.Equal("roleplay", report.Mode);
        Assert.Equal(ExpectedModelSequence, report.Run.ProviderTraces.Select(trace => trace.Model).ToArray());
        Assert.Equal(8, report.Run.ProviderTraces.Count);
        Assert.Equal(8, report.UsageSummary.TotalRequests);
        Assert.Equal(1, report.Run.AppTrace?.ToolRounds);
        Assert.Equal("idle", report.Run.AppTrace?.WriterState);
        Assert.Equal(
            new[]
            {
                "librarian:2",
                "narrative-director:2",
                "orchestrator:2",
                "prose-writer:2",
            },
            GetAgentCounts(report.UsageSummary));
    }

    private static string[] GetAgentCounts(SessionUsageSummary usageSummary)
    {
        return usageSummary.ByAgent
            .OrderBy(entry => entry.AgentName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.AgentName}:{entry.RequestCount}")
            .ToArray();
    }

    private sealed class InteractiveCostProfileResponseSource : IHarnessResponseSource
    {
        public string ScenarioName => "interactive-cost-profile";

        public IReadOnlyList<string> Models { get; } =
        [
            "librarian-model",
            "narrative-director-model",
            "orchestrator-model",
            "prose-writer-model",
        ];

        public Task<HarnessResponsePlan> GetNextResponseAsync(HarnessObservedRequest request, CancellationToken ct)
        {
            var parsed = HarnessWorkerRequest.Parse(request);

            return request.Model switch
            {
                "orchestrator-model" => Task.FromResult(BuildOrchestratorResponse(parsed)),
                "narrative-director-model" => Task.FromResult(BuildNarrativeDirectorResponse(parsed)),
                "prose-writer-model" => Task.FromResult(BuildProseWriterResponse(parsed)),
                "librarian-model" => Task.FromResult(BuildLibrarianResponse(parsed)),
                _ => throw new InvalidOperationException(
                    $"Interactive cost profile source does not handle model '{request.Model ?? "(null)"}'."),
            };
        }

        private static HarnessResponsePlan BuildOrchestratorResponse(HarnessWorkerRequest request)
        {
            if (!request.HasAnyToolResults)
            {
                return CreateToolCallResponse(
                    request,
                    "orchestrator_direct_scene",
                    "direct_scene",
                    request.SerializeJson(new
                    {
                        user_message = request.LatestUserContent ?? "Continue the grounded scene.",
                    }),
                    new HarnessUsage(180, 35));
            }

            request.TryGetLatestToolResult("direct_scene", out var directedSceneContent);
            var finalText = string.IsNullOrWhiteSpace(directedSceneContent)
                ? "Grounded scene handoff complete."
                : directedSceneContent!;

            return CreateTextResponse(request, finalText, new HarnessUsage(260, 95));
        }

        private static HarnessResponsePlan BuildNarrativeDirectorResponse(HarnessWorkerRequest request)
        {
            if (request.TryGetLatestToolResult("write_prose", out var proseContent)
                && !string.IsNullOrWhiteSpace(proseContent))
            {
                return CreateTextResponse(request, proseContent!, new HarnessUsage(245, 80));
            }

            var latestUser = request.LatestUserContent ?? "Continue the scene.";
            var isRoleplay = latestUser.Contains("Aurora", StringComparison.OrdinalIgnoreCase)
                && latestUser.Contains("what did you hide", StringComparison.OrdinalIgnoreCase);
            var sceneDescription = isRoleplay
                ? "Reply in character as Aurora when asked what she hid in the conservatory wall."
                : "Write the moment Aurora realizes the sapphire ring was hidden in the conservatory wall.";
            var toneNotes = isRoleplay
                ? "Poised, intimate, and slightly guarded."
                : "Elegant restraint with a flash of private shock.";

            return CreateToolCallResponse(
                request,
                new[]
                {
                    new HarnessToolCallPlan(
                        "director_lore",
                        "query_lore",
                        request.SerializeJson(new
                        {
                            query = "Aurora sapphire ring conservatory wall composure",
                        })),
                    new HarnessToolCallPlan(
                        "director_notes",
                        "update_narrative_state",
                        request.SerializeJson(new
                        {
                            director_notes = "Aurora keeps public composure while the hidden sapphire ring changes the emotional temperature of the scene.",
                        })),
                    new HarnessToolCallPlan(
                        "director_prose",
                        "write_prose",
                        request.SerializeJson(new
                        {
                            scene_description = sceneDescription,
                            tone_notes = toneNotes,
                        })),
                },
                usage: new HarnessUsage(220, 90));
        }

        private static HarnessResponsePlan BuildProseWriterResponse(HarnessWorkerRequest request)
        {
            if (request.TryGetLatestToolResult("query_lore", out var loreResult)
                && !string.IsNullOrWhiteSpace(loreResult))
            {
                var prose = BuildGroundedProse(request.LatestUserContent, loreResult!);
                return CreateTextResponse(request, prose, new HarnessUsage(210, 180));
            }

            var latestUser = request.LatestUserContent ?? "";
            var query = latestUser.Contains("Reply in character as Aurora", StringComparison.OrdinalIgnoreCase)
                ? "Aurora hidden ring conservatory wall response voice"
                : "Aurora sapphire ring conservatory wall composure";

            return CreateToolCallResponse(
                request,
                "writer_lore",
                "query_lore",
                request.SerializeJson(new { query }),
                new HarnessUsage(160, 70));
        }

        private static HarnessResponsePlan BuildLibrarianResponse(HarnessWorkerRequest request)
        {
            var response = request.SerializeJson(new
            {
                relevant_passages = new[]
                {
                    "Aurora once hid a sapphire ring inside the conservatory wall.",
                    "Aurora is classically elegant and composed under social pressure.",
                },
                source_files = new[]
                {
                    "world.md",
                    "world.md",
                },
                confidence = "high",
            });

            return CreateTextResponse(request, response, new HarnessUsage(140, 45));
        }

        private static string BuildGroundedProse(string? latestUserContent, string loreResult)
        {
            var isRoleplay = latestUserContent?.Contains(
                "Reply in character as Aurora",
                StringComparison.OrdinalIgnoreCase) == true;

            var loreEcho = ExtractLoreEcho(loreResult);
            if (isRoleplay)
            {
                return string.IsNullOrWhiteSpace(loreEcho)
                    ? """
                      Aurora's gaze does not waver. "A sapphire ring," she says at last, her voice low and even. "I hid it where panic could not make a spectacle of me."

                      One gloved fingertip marks the direction of the conservatory wall without quite pointing. "Composure was easier to keep when the secret had somewhere solid to wait."
                      """
                    : $"""
                      Aurora's gaze does not waver. "A sapphire ring," she says at last, her voice low and even. "I hid it where panic could not make a spectacle of me."

                      One gloved fingertip marks the direction of the conservatory wall without quite pointing. "Composure was easier to keep when the secret had somewhere solid to wait." {loreEcho}
                      """;
            }

            return string.IsNullOrWhiteSpace(loreEcho)
                ? """
                  Aurora's hand stilled against the cold seam in the conservatory wall. For one suspended instant the hidden sapphire ring seemed to pulse beneath her fingertips, a private truth waiting under all the evening's practiced grace.

                  She lowered her lashes before anyone could read the shock that struck clean through her. When she turned back toward the ballroom, her composure was immaculate again, as if elegance itself had risen to shield her.
                  """
                : $"""
                  Aurora's hand stilled against the cold seam in the conservatory wall. For one suspended instant the hidden sapphire ring seemed to pulse beneath her fingertips, a private truth waiting under all the evening's practiced grace.

                  She lowered her lashes before anyone could read the shock that struck clean through her. When she turned back toward the ballroom, her composure was immaculate again, as if elegance itself had risen to shield her. {loreEcho}
                  """;
        }

        private static string ExtractLoreEcho(string loreResult)
        {
            try
            {
                using var document = JsonDocument.Parse(loreResult);
                if (!document.RootElement.TryGetProperty("relevant_passages", out var passagesElement)
                    || passagesElement.ValueKind != JsonValueKind.Array)
                {
                    return "";
                }

                var passages = passagesElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                return passages.Count == 0
                    ? ""
                    : $"Lore confirmed: {string.Join(" ", passages)}";
            }
            catch (JsonException)
            {
                return "";
            }
        }

        private static HarnessResponsePlan CreateToolCallResponse(
            HarnessWorkerRequest request,
            string toolId,
            string toolName,
            string argumentsJson,
            HarnessUsage usage)
        {
            return CreateToolCallResponse(
                request,
                new HarnessToolCallPlan(toolId, toolName, argumentsJson),
                usage);
        }

        private static HarnessResponsePlan CreateToolCallResponse(
            HarnessWorkerRequest request,
            HarnessToolCallPlan toolCall,
            HarnessUsage usage)
        {
            return CreateToolCallResponse(request, [toolCall], usage);
        }

        private static HarnessResponsePlan CreateToolCallResponse(
            HarnessWorkerRequest request,
            IReadOnlyList<HarnessToolCallPlan> toolCalls,
            HarnessUsage usage)
        {
            if (request.ObservedRequest.Stream)
            {
                return new HarnessResponsePlan
                {
                    Mode = HarnessResponseMode.ScriptedStream,
                    ExpectedModel = request.Model,
                    StreamEvents =
                    [
                        new HarnessStreamEventPlan
                        {
                            ToolCalls = toolCalls
                                .Select((toolCall, index) => new HarnessToolCallDeltaPlan(
                                    index,
                                    toolCall.Id,
                                    toolCall.Name,
                                    toolCall.ArgumentsJson))
                                .ToList(),
                            FinishReason = "tool_calls",
                            Usage = usage,
                        },
                    ],
                    Usage = usage,
                    FinishReason = "tool_calls",
                };
            }

            return new HarnessResponsePlan
            {
                Mode = HarnessResponseMode.ScriptedComplete,
                ExpectedModel = request.Model,
                Message = new HarnessAssistantMessage
                {
                    ToolCalls = toolCalls,
                },
                Usage = usage,
                FinishReason = "tool_calls",
            };
        }

        private static HarnessResponsePlan CreateTextResponse(
            HarnessWorkerRequest request,
            string content,
            HarnessUsage usage)
        {
            if (request.ObservedRequest.Stream)
            {
                return new HarnessResponsePlan
                {
                    Mode = HarnessResponseMode.ScriptedStream,
                    ExpectedModel = request.Model,
                    StreamEvents =
                    [
                        new HarnessStreamEventPlan
                        {
                            TextDelta = content,
                            FinishReason = "stop",
                            Usage = usage,
                        },
                    ],
                    Usage = usage,
                    FinishReason = "stop",
                };
            }

            return new HarnessResponsePlan
            {
                Mode = HarnessResponseMode.ScriptedComplete,
                ExpectedModel = request.Model,
                Message = new HarnessAssistantMessage
                {
                    Content = content,
                },
                Usage = usage,
                FinishReason = "stop",
            };
        }
    }
}
