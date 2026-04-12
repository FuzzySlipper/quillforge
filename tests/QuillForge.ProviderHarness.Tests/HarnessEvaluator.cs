namespace QuillForge.ProviderHarness.Tests;

public enum HarnessEvaluationStatus
{
    Passed,
    Failed,
}

public enum HarnessFindingSeverity
{
    Warning,
    Error,
}

public sealed record HarnessFinding(
    string Code,
    HarnessFindingSeverity Severity,
    string Expected,
    string Actual,
    IReadOnlyList<string> Evidence);

public sealed record HarnessAssertionResult(
    string AssertionName,
    bool Passed,
    IReadOnlyList<HarnessFinding> Findings);

public sealed record HarnessEvaluationResult(
    HarnessEvaluationStatus Status,
    IReadOnlyList<HarnessAssertionResult> AssertionResults,
    IReadOnlyList<HarnessFinding> Findings);

public interface IHarnessAssertion
{
    string Name { get; }

    HarnessAssertionResult Evaluate(DualSidedHarnessRun run);
}

public sealed class HarnessEvaluator
{
    public HarnessEvaluationResult Evaluate(
        DualSidedHarnessRun run,
        IEnumerable<IHarnessAssertion> assertions)
    {
        var results = assertions.Select(assertion => assertion.Evaluate(run)).ToList();
        var findings = results.SelectMany(result => result.Findings).ToList();
        var status = findings.Any(finding => finding.Severity == HarnessFindingSeverity.Error)
            ? HarnessEvaluationStatus.Failed
            : HarnessEvaluationStatus.Passed;

        return new HarnessEvaluationResult(status, results, findings);
    }
}

public sealed class ExpectedProviderRequestSectionAssertion : IHarnessAssertion
{
    private readonly string _expectedText;

    public ExpectedProviderRequestSectionAssertion(string expectedText, string? name = null)
    {
        _expectedText = expectedText;
        Name = name ?? $"Provider request contains '{expectedText}'";
    }

    public string Name { get; }

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        foreach (var trace in run.ProviderTraces)
        {
            if (trace.RawRequestBody.Contains(_expectedText, StringComparison.OrdinalIgnoreCase))
            {
                return new HarnessAssertionResult(Name, true, []);
            }
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "provider_request_missing_section",
                    HarnessFindingSeverity.Error,
                    $"At least one provider request should contain '{_expectedText}'.",
                    "None of the observed provider requests contained the expected section text.",
                    run.ProviderTraces.Select((_, index) => $"provider[{index}].rawRequestBody").ToList()),
            ]);
    }
}

public sealed class ExpectedToolMirroredAcrossBoundaryAssertion : IHarnessAssertion
{
    public string Name => "Provider-emitted tools are surfaced app-side";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var providerTools = run.ProviderTraces
            .SelectMany(trace => trace.EmittedToolCalls)
            .Select(tool => tool.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (providerTools.Count == 0)
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        var appTools = run.AppTrace?.Tools
            .Select(tool => tool.Name)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var findings = new List<HarnessFinding>();
        foreach (var providerTool in providerTools)
        {
            if (!appTools.Contains(providerTool))
            {
                findings.Add(new HarnessFinding(
                    "provider_tool_not_mirrored",
                    HarnessFindingSeverity.Error,
                    $"Tool '{providerTool}' should appear in the app-side trace when the provider emitted it.",
                    $"The provider emitted '{providerTool}', but the app-side trace did not surface it.",
                    ["provider.emittedToolCalls", "app.tools"]));
            }
        }

        return new HarnessAssertionResult(Name, findings.Count == 0, findings);
    }
}

public sealed class ExpectedFinalContentConsistencyAssertion : IHarnessAssertion
{
    public string Name => "Provider-visible and app-visible final content stay aligned";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var providerFinalContent = run.ProviderTraces
            .Select(trace => trace.FinalContent)
            .LastOrDefault(content => !string.IsNullOrWhiteSpace(content));
        var appFinalContent = run.AppTrace?.FinalContent;

        if (string.IsNullOrWhiteSpace(providerFinalContent) || appFinalContent is null)
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        if (string.Equals(providerFinalContent.Trim(), appFinalContent.Trim(), StringComparison.Ordinal))
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "final_content_mismatch",
                    HarnessFindingSeverity.Error,
                    "The final app-visible content should match the final provider-visible content for the run.",
                    $"Provider final content was '{providerFinalContent}', while app final content was '{appFinalContent}'.",
                    ["provider.finalContent", "app.finalContent"]),
            ]);
    }
}

public sealed class ExpectedPersistedAssistantContentAssertion : IHarnessAssertion
{
    public string Name => "Final assistant content is persisted to the session trace";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var appTrace = run.AppTrace;
        if (appTrace is null || string.IsNullOrWhiteSpace(appTrace.FinalContent))
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        var persistedAssistantContents = appTrace.PersistedMessages
            .Where(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Content)
            .ToList();

        if (persistedAssistantContents.Any(content => string.Equals(content, appTrace.FinalContent, StringComparison.Ordinal)))
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "assistant_content_not_persisted",
                    HarnessFindingSeverity.Error,
                    "The final app-visible assistant content should appear in the persisted session snapshot.",
                    $"Final content '{appTrace.FinalContent}' was not found in persisted assistant messages.",
                    ["app.finalContent", "app.persistedMessages"]),
            ]);
    }
}

public sealed class ExpectedArtifactPresenceAssertion : IHarnessAssertion
{
    private readonly string _relativePath;

    public ExpectedArtifactPresenceAssertion(string relativePath)
    {
        _relativePath = relativePath.Replace('\\', '/');
    }

    public string Name => $"Artifact '{_relativePath}' exists";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var artifact = run.ArtifactTrace?.Snapshots
            .FirstOrDefault(snapshot => string.Equals(snapshot.RelativePath, _relativePath, StringComparison.Ordinal));

        if (artifact?.Exists == true)
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "artifact_missing",
                    HarnessFindingSeverity.Error,
                    $"Artifact '{_relativePath}' should exist in the captured artifact trace.",
                    artifact is null
                        ? $"Artifact '{_relativePath}' was not captured in the artifact trace."
                        : $"Artifact '{_relativePath}' was captured but missing on disk.",
                    ["artifacts"]),
            ]);
    }
}

public sealed class ExpectedStopReasonConsistencyAssertion : IHarnessAssertion
{
    public string Name => "Provider and app stop reasons stay aligned";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var providerStopReason = run.ProviderTraces
            .Select(trace => trace.FinishReason)
            .LastOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        var appStopReason = run.AppTrace?.StopReason;

        if (string.IsNullOrWhiteSpace(providerStopReason) || string.IsNullOrWhiteSpace(appStopReason))
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        var normalizedProviderStopReason = NormalizeProviderStopReason(providerStopReason!);
        var normalizedAppStopReason = NormalizeAppStopReason(appStopReason!);
        if (string.Equals(normalizedProviderStopReason, normalizedAppStopReason, StringComparison.Ordinal))
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "stop_reason_mismatch",
                    HarnessFindingSeverity.Warning,
                    $"Provider stop reason '{normalizedProviderStopReason}' should align with app stop reason '{normalizedAppStopReason}'.",
                    $"Observed provider stop reason '{providerStopReason}' and app stop reason '{appStopReason}'.",
                    ["provider.finishReason", "app.stopReason"]),
            ]);
    }

    private static string NormalizeProviderStopReason(string providerStopReason)
    {
        return providerStopReason switch
        {
            "stop" => "end_turn",
            "tool_calls" => "tool_use",
            "length" => "max_tokens",
            _ => providerStopReason,
        };
    }

    private static string NormalizeAppStopReason(string appStopReason)
    {
        return appStopReason;
    }
}

public sealed class ExpectedWorkerRoleObservedAssertion : IHarnessAssertion
{
    private readonly string[] _expectedRoles;

    public ExpectedWorkerRoleObservedAssertion(params HarnessWorkerRole[] expectedRoles)
    {
        _expectedRoles = expectedRoles
            .Select(role => role.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public string Name => $"Worker roles observed: {string.Join(", ", _expectedRoles)}";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var observedRoles = run.ProviderTraces
            .Select(trace => trace.WorkerTrace?.Role)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var findings = new List<HarnessFinding>();
        foreach (var expectedRole in _expectedRoles)
        {
            if (observedRoles.Contains(expectedRole))
            {
                continue;
            }

            findings.Add(new HarnessFinding(
                "worker_role_not_observed",
                HarnessFindingSeverity.Error,
                $"Provider traces should include worker role '{expectedRole}'.",
                $"Worker role '{expectedRole}' was not observed in any provider trace.",
                ["provider.workerTrace"]));
        }

        return new HarnessAssertionResult(Name, findings.Count == 0, findings);
    }
}

public sealed class ExpectedForgeManifestStageAssertion : IHarnessAssertion
{
    private readonly string _expectedStage;
    private readonly bool? _expectedPaused;

    public ExpectedForgeManifestStageAssertion(string expectedStage, bool? expectedPaused = null)
    {
        _expectedStage = expectedStage;
        _expectedPaused = expectedPaused;
    }

    public string Name => $"Forge manifest reaches stage '{_expectedStage}'";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var manifest = run.ForgeManifest;
        if (manifest is null)
        {
            return new HarnessAssertionResult(
                Name,
                false,
                [
                    new HarnessFinding(
                        "forge_manifest_missing",
                        HarnessFindingSeverity.Error,
                        "A typed forge manifest snapshot should be captured for the run.",
                        "The run did not include a forge manifest snapshot.",
                        ["forgeManifest"]),
                ]);
        }

        var findings = new List<HarnessFinding>();
        if (!string.Equals(manifest.Stage, _expectedStage, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new HarnessFinding(
                "forge_stage_mismatch",
                HarnessFindingSeverity.Error,
                $"Forge manifest stage should be '{_expectedStage}'.",
                $"Observed stage '{manifest.Stage}'.",
                ["forgeManifest.stage"]));
        }

        if (_expectedPaused.HasValue && manifest.Paused != _expectedPaused.Value)
        {
            findings.Add(new HarnessFinding(
                "forge_pause_state_mismatch",
                HarnessFindingSeverity.Error,
                $"Forge manifest paused should be '{_expectedPaused.Value}'.",
                $"Observed paused '{manifest.Paused}'.",
                ["forgeManifest.paused"]));
        }

        return new HarnessAssertionResult(Name, findings.Count == 0, findings);
    }
}

public sealed class ExpectedForgeChapterDiscoveredAssertion : IHarnessAssertion
{
    private readonly string _chapterId;

    public ExpectedForgeChapterDiscoveredAssertion(string chapterId)
    {
        _chapterId = chapterId;
    }

    public string Name => $"Forge chapter '{_chapterId}' is discovered";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var manifest = run.ForgeManifest;
        if (manifest?.Chapters.ContainsKey(_chapterId) == true)
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "forge_chapter_missing",
                    HarnessFindingSeverity.Error,
                    $"Forge manifest should contain chapter '{_chapterId}'.",
                    manifest is null
                        ? "No forge manifest snapshot was captured."
                        : $"Chapter '{_chapterId}' was not present in the manifest snapshot.",
                    ["forgeManifest.chapters"]),
            ]);
    }
}

public sealed class ExpectedForgePauseSurfacedAssertion : IHarnessAssertion
{
    public string Name => "Forge pause is surfaced app-side and persisted in status";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var forgeTrace = run.ForgeTrace;
        var hasPauseEvent = forgeTrace?.Events.Any(evt => evt is ForgePausedObserved) == true;
        var statusPaused = forgeTrace?.Status?.Paused;

        if (hasPauseEvent && statusPaused == true)
        {
            return new HarnessAssertionResult(Name, true, []);
        }

        return new HarnessAssertionResult(
            Name,
            false,
            [
                new HarnessFinding(
                    "forge_pause_not_surfaced",
                    HarnessFindingSeverity.Error,
                    "A paused forge run should emit a pause event and persist paused=true in status.",
                    $"Pause event observed={hasPauseEvent}, status.paused={statusPaused?.ToString() ?? "(null)"}.",
                    ["forgeTrace.events", "forgeTrace.status"]),
            ]);
    }
}

public sealed class ExpectedForgeStatusMatchesManifestAssertion : IHarnessAssertion
{
    public string Name => "Forge status surface matches persisted manifest snapshot";

    public HarnessAssertionResult Evaluate(DualSidedHarnessRun run)
    {
        var manifest = run.ForgeManifest;
        var status = run.ForgeTrace?.Status;
        if (manifest is null || status is null)
        {
            return new HarnessAssertionResult(
                Name,
                false,
                [
                    new HarnessFinding(
                        "forge_status_or_manifest_missing",
                        HarnessFindingSeverity.Error,
                        "Both forge manifest and forge status snapshots should be present.",
                        $"Manifest present={manifest is not null}, status present={status is not null}.",
                        ["forgeManifest", "forgeTrace.status"]),
                ]);
        }

        var findings = new List<HarnessFinding>();
        if (!string.Equals(manifest.Stage, status.Stage, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new HarnessFinding(
                "forge_status_stage_mismatch",
                HarnessFindingSeverity.Error,
                "Status stage should match the manifest stage.",
                $"Manifest stage '{manifest.Stage}' != status stage '{status.Stage}'.",
                ["forgeManifest.stage", "forgeTrace.status.stage"]));
        }

        if (manifest.Paused != status.Paused)
        {
            findings.Add(new HarnessFinding(
                "forge_status_paused_mismatch",
                HarnessFindingSeverity.Error,
                "Status paused should match the manifest paused state.",
                $"Manifest paused '{manifest.Paused}' != status paused '{status.Paused}'.",
                ["forgeManifest.paused", "forgeTrace.status.paused"]));
        }

        if (manifest.ChapterCount != status.ChapterCount)
        {
            findings.Add(new HarnessFinding(
                "forge_status_chapter_count_mismatch",
                HarnessFindingSeverity.Error,
                "Status chapterCount should match the manifest chapterCount.",
                $"Manifest chapterCount '{manifest.ChapterCount}' != status chapterCount '{status.ChapterCount}'.",
                ["forgeManifest.chapterCount", "forgeTrace.status.chapterCount"]));
        }

        return new HarnessAssertionResult(Name, findings.Count == 0, findings);
    }
}
