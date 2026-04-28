namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessGameScenarioTests
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
}
