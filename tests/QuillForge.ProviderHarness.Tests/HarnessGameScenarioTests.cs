using System.Text.RegularExpressions;
using Den.RulesEngine.Werewolf;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.ProviderHarness.Tests;

public sealed partial class HarnessGameScenarioTests
{
    [Fact]
    public async Task WerewolfVillageWinScenario_CapturesStableGameTraceAndFailureTaxonomy()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "quillforge-game-harness-tests", Guid.NewGuid().ToString("N"));
        var artifactStore = new HarnessRunArtifactStore("game-werewolf-village-win", artifactRoot);
        var runner = new HarnessGameScenarioRunner(artifactStore);

        var report = await runner.RunWerewolfVillageWinAsync();

        Assert.Equal("game-werewolf-village-win", report.ScenarioName);
        Assert.Equal(artifactStore.RunId, report.GameTrace.RunId);
        Assert.Equal("scripted-fake-completion", report.GameTrace.DeterminismMode);
        Assert.Contains("prompt-level deterministic", report.GameTrace.DeterminismDescription, StringComparison.OrdinalIgnoreCase);
        Assert.False(report.GameTrace.LiveProviderRun);
        Assert.Equal("Ended", report.GameTrace.Status);
        Assert.Equal("villagers_win", report.GameTrace.FinalOutcome);
        Assert.Equal(4, report.GameTrace.Agents.Count);
        var scriptedAgents = report.GameTrace.Agents.Where(agent => !string.IsNullOrWhiteSpace(agent.ProviderAlias)).ToArray();
        Assert.Equal(3, scriptedAgents.Length);
        Assert.All(scriptedAgents, agent =>
        {
            Assert.StartsWith("scripted-", agent.ProviderAlias, StringComparison.Ordinal);
            Assert.NotNull(agent.PromptCursor);
        });
        Assert.Contains(report.GameTrace.Actions, action => action.Outcome == "Rejected" && action.ReasonCode == "parse-fail");
        Assert.Contains(report.GameTrace.Actions, action => action.Outcome == "Applied" && action.ChoiceName is not null);
        Assert.NotEmpty(report.GameTrace.PromptEnvelopes);
        Assert.Contains(report.GameTrace.EngineEvents, item => item.EventType == "WerewolfRoleRevealedEvent");
        Assert.Contains(report.GameTrace.EngineEvents, item => item.EventType == "WerewolfWinConditionResolvedEvent");
        Assert.Contains(report.GameTrace.FailureSurface.AgentResponseRejected, item => item.ReasonCode == "parse-fail");
        Assert.Contains(report.GameTrace.FailureSurface.NoActionTaken, item => item.ReasonCode == "parse-fail");
        Assert.DoesNotContain(report.GameTrace.PublicFeed, entry => entry.Summary?.StartsWith("Your role is", StringComparison.Ordinal) == true);
        Assert.Contains(report.GameTrace.PrivateFeedByParticipant.Values.SelectMany(entries => entries), entry => entry.Summary?.StartsWith("Your role is", StringComparison.Ordinal) == true);
        Assert.True(report.GameTrace.Usage.PromptTokens > 0);
        Assert.True(report.GameTrace.Usage.CompletionTokens > 0);

        Assert.NotNull(report.PersistedReport);
        Assert.Equal("game", report.PersistedReport!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(report.PersistedReport.JsonReportPath));
        Assert.False(string.IsNullOrWhiteSpace(report.PersistedReport.MarkdownReportPath));

        var jsonPath = Path.Combine(artifactStore.RunDirectory, report.PersistedReport.AppTraceFile!.Replace('/', Path.DirectorySeparatorChar));
        var markdownPath = Path.Combine(artifactStore.RunDirectory, report.PersistedReport.MarkdownReportPath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));

        var json = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("agentResponseRejected", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noActionTaken", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("villagers_win", json, StringComparison.Ordinal);

        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("Determinism", markdown, StringComparison.Ordinal);
        Assert.Contains("scripted-fake-completion", markdown, StringComparison.Ordinal);
        Assert.Contains("Final outcome: `villagers_win`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbortEdgeCaseScenario_CapturesCommandRejectionAndAbortFailureSurface()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "quillforge-game-harness-tests", Guid.NewGuid().ToString("N"));
        var artifactStore = new HarnessRunArtifactStore("game-werewolf-abort-edge-case", artifactRoot);
        var runner = new HarnessGameScenarioRunner(artifactStore);

        var report = await runner.RunWerewolfAbortEdgeCaseAsync();

        Assert.Equal("game-werewolf-abort-edge-case", report.ScenarioName);
        Assert.Equal("scripted-fake-completion", report.GameTrace.DeterminismMode);
        Assert.False(report.GameTrace.LiveProviderRun);
        Assert.Equal("Aborted", report.GameTrace.Status);
        Assert.Null(report.GameTrace.FinalOutcome);
        Assert.Contains(report.GameTrace.EngineEvents, item =>
            item.EventType == "IntentCommandRejectedEvent" && item.ReasonCode == "unknown_pending_input");
        Assert.Contains(report.GameTrace.EngineEvents, item =>
            item.EventType == "GameAbortedEvent" && item.ReasonCode == "harness-abort-edge-case");
        Assert.Contains(report.GameTrace.RuntimeEvents, item => item.EventName == "GameRuntimeAbortedEvent");
        Assert.Contains(report.GameTrace.FailureSurface.IntentCommandRejected, item =>
            item.ReasonCode == "unknown_pending_input" && item.Reason.Contains("Pending input", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.GameTrace.FailureSurface.GameAborted, item => item.ReasonCode == "harness-abort-edge-case");

        var jsonPath = Path.Combine(artifactStore.RunDirectory, report.PersistedReport!.AppTraceFile!.Replace('/', Path.DirectorySeparatorChar));
        var json = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("intentCommandRejected", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gameAborted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("harness-abort-edge-case", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExploratoryNightEntryPoint_AcceptsFakeCompletionServiceForSmokeGuard()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "quillforge-game-harness-tests", Guid.NewGuid().ToString("N"));
        var artifactStore = new HarnessRunArtifactStore("game-werewolf-exploratory-smoke", artifactRoot);
        var runner = new HarnessGameScenarioRunner(artifactStore);
        var template = HarnessGameScenarioRunner.CreateWerewolfHarnessTemplate(memoryTokenBudget: 64) with
        {
            TemplateId = "werewolf-harness-exploratory-smoke",
        };

        var report = await runner.RunWerewolfExploratoryNightAsync(
            new ExploratorySmokeCompletionService(),
            template,
            scenarioName: "game-werewolf-exploratory-smoke");

        Assert.Equal("game-werewolf-exploratory-smoke", report.ScenarioName);
        Assert.Equal("live-provider-exploratory", report.GameTrace.DeterminismMode);
        Assert.True(report.GameTrace.LiveProviderRun);
        Assert.NotEmpty(report.GameTrace.PromptEnvelopes);
        Assert.Contains(report.GameTrace.Actions, action => action.Outcome == "Applied" && action.ChoiceName == WerewolfConstants.SkipNightChoice);
        Assert.NotNull(report.PersistedReport);
    }

    [Fact]
    public async Task RoundBoundaryMemoryScenario_CapturesMemorySummariesCursorsAndTrimFlags()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), "quillforge-game-harness-tests", Guid.NewGuid().ToString("N"));
        var artifactStore = new HarnessRunArtifactStore("game-werewolf-round-memory", artifactRoot);
        var runner = new HarnessGameScenarioRunner(artifactStore);

        var report = await runner.RunWerewolfMemoryAfterRoundAsync();

        Assert.Equal("game-werewolf-round-memory", report.ScenarioName);
        Assert.NotEmpty(report.GameTrace.MemorySummaries);
        Assert.All(report.GameTrace.MemorySummaries, summary =>
        {
            Assert.Equal("Recorded", summary.Outcome);
            Assert.Equal("summary-trimmed", summary.ReasonCode);
            Assert.True(summary.ExceededTokenBudget);
            Assert.True(summary.Trimmed);
            Assert.NotNull(summary.SummaryContentHash);
        });
        Assert.All(report.GameTrace.Agents.Where(agent => !string.IsNullOrWhiteSpace(agent.ProviderAlias)), agent =>
        {
            Assert.NotNull(agent.Memory);
            Assert.True(agent.Memory!.Revision >= 1);
            Assert.True(agent.Memory.LastSummarizedRoundNumber >= 1);
            Assert.NotNull(agent.PromptCursor);
            Assert.True(agent.PromptCursor!.PublicEngineEventSequence > 0);
        });
        Assert.Contains(report.GameTrace.EngineEvents, item => item.EventType == "RoundEndedEvent" && item.ReasonCode == "harness-round-boundary");
        Assert.NotEmpty(report.GameTrace.FailureSurface.MemoryDecisionFlags);
        Assert.Contains(report.GameTrace.PublicFeed, entry => entry.Text?.Contains("compare notes", StringComparison.OrdinalIgnoreCase) == true);

        var jsonPath = Path.Combine(artifactStore.RunDirectory, report.PersistedReport!.AppTraceFile!.Replace('/', Path.DirectorySeparatorChar));
        var json = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("memorySummaries", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("summary-trimmed", json, StringComparison.Ordinal);
        Assert.Contains("promptCursor", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed partial class ExploratorySmokeCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            var prompt = request.Messages.Count == 0 ? string.Empty : request.Messages[0].Content.GetText();
            var pendingInputId = ExtractPendingInputId(prompt);
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent("{\"accepted\":true,\"pendingInputId\":\"" + pendingInputId + "\",\"choiceName\":\"" + WerewolfConstants.SkipNightChoice + "\",\"message\":\"smoke guard choice\"}"),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(13, 7),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }

        private static string ExtractPendingInputId(string prompt)
        {
            var match = PendingInputLineRegex().Match(prompt);
            return match.Success ? match.Groups[1].Value : "missing-pending-input";
        }

        [GeneratedRegex(@"pendingInputId: ([^\r\n]+)")]
        private static partial Regex PendingInputLineRegex();
    }
}
