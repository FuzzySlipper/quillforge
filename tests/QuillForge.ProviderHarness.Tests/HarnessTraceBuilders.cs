namespace QuillForge.ProviderHarness.Tests;

public static class HarnessAppTraceBuilder
{
    public static HarnessAppTrace FromCollectedStream(
        HarnessCollectedAppStream stream,
        HarnessCollectedSessionSnapshot? sessionSnapshot = null)
    {
        var typedEvents = new List<HarnessAppTraceEvent>();
        var textDeltas = new List<string>();
        var reasoningDeltas = new List<string>();
        var tools = new List<HarnessToolCallTrace>();
        var diagnostics = new List<HarnessDiagnosticTrace>();

        foreach (var item in stream.Events)
        {
            switch (item.Type)
            {
                case "text_delta":
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        textDeltas.Add(item.Text);
                        typedEvents.Add(new AppTextDeltaObserved(item.Text));
                    }
                    break;
                case "reasoning_delta":
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        reasoningDeltas.Add(item.Text);
                        typedEvents.Add(new AppReasoningDeltaObserved(item.Text));
                    }
                    break;
                case "tool":
                    if (!string.IsNullOrEmpty(item.ToolName))
                    {
                        var tool = new HarnessToolCallTrace(item.ToolId ?? "", item.ToolName, "", null);
                        tools.Add(tool);
                        typedEvents.Add(new AppToolEventObserved(item.ToolName, item.ToolId));
                    }
                    break;
                case "diagnostic":
                    if (!string.IsNullOrEmpty(item.Message))
                    {
                        var diagnostic = new HarnessDiagnosticTrace(item.Category ?? "unknown", item.Message, item.Level);
                        diagnostics.Add(diagnostic);
                        typedEvents.Add(new AppDiagnosticObserved(diagnostic.Category, diagnostic.Message, diagnostic.Level));
                    }
                    break;
                case "done":
                    typedEvents.Add(new AppDoneObserved(item.StopReason ?? stream.StopReason, item.Usage));
                    break;
                default:
                    typedEvents.Add(new AppUnknownEventObserved(item.Type));
                    break;
            }
        }

        return new HarnessAppTrace
        {
            SessionId = stream.SessionId,
            Mode = stream.Mode,
            FinalContent = stream.FinalContent,
            FinalReasoning = stream.FinalReasoning,
            StopReason = stream.StopReason,
            MessageCount = stream.MessageCount,
            ToolRounds = stream.ToolRounds,
            Usage = stream.Usage,
            Events = typedEvents,
            TextDeltas = textDeltas,
            ReasoningDeltas = reasoningDeltas,
            Tools = tools,
            Diagnostics = diagnostics,
            PersistedMessages = sessionSnapshot is null
                ? []
                : sessionSnapshot.Messages.Select(ToPersistedTrace).ToList(),
            WriterState = stream.WriterState,
        };
    }

    private static HarnessPersistedMessageTrace ToPersistedTrace(HarnessCollectedSessionMessage message)
    {
        return new HarnessPersistedMessageTrace
        {
            Id = message.Id,
            Role = message.Role,
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            ParentId = message.ParentId,
            Reasoning = message.Reasoning,
            Variants = message.Variants
                .Select(variant => new HarnessPersistedMessageVariantTrace(
                    variant.Content,
                    variant.CreatedAt,
                    variant.Reasoning))
                .ToList(),
        };
    }

    public static HarnessForgeAppTrace FromCollectedForgeRun(HarnessCollectedForgeRun run)
    {
        var typedEvents = new List<HarnessForgeTraceEvent>();

        foreach (var item in run.Events)
        {
            switch (item.Type)
            {
                case "stage_started":
                    typedEvents.Add(new ForgeStageStartedObserved(item.Message ?? ""));
                    break;
                case "stage_completed":
                    typedEvents.Add(new ForgeStageCompletedObserved(item.Message ?? ""));
                    break;
                case "chapter":
                    typedEvents.Add(new ForgeChapterObserved(
                        item.Chapter ?? "",
                        item.Status ?? "",
                        item.Detail));
                    break;
                case "progress":
                    typedEvents.Add(new ForgeProgressObserved(item.Message ?? "", item.Source));
                    break;
                case "pause":
                    typedEvents.Add(new ForgePausedObserved(item.Message ?? ""));
                    break;
                case "complete":
                    typedEvents.Add(new ForgeCompletedObserved(
                        item.Message ?? "",
                        item.ChaptersComplete,
                        item.TotalTokens));
                    break;
                case "error":
                    typedEvents.Add(new ForgeErrorObserved(item.Message ?? "", item.Source));
                    break;
                default:
                    typedEvents.Add(new ForgeUnknownObserved(item.Type, item.Message));
                    break;
            }
        }

        return new HarnessForgeAppTrace
        {
            ProjectName = run.ProjectName,
            Operation = run.Operation,
            Events = typedEvents,
            FinalEventType = run.FinalEventType,
            Status = run.Status,
        };
    }

    public static HarnessForgeManifestSnapshot FromManifest(QuillForge.Core.Models.ForgeManifest manifest)
    {
        return new HarnessForgeManifestSnapshot
        {
            ProjectName = manifest.ProjectName,
            Stage = manifest.Stage.ToString(),
            ChapterCount = manifest.ChapterCount,
            Paused = manifest.Paused,
            Chapters = manifest.Chapters.ToDictionary(
                pair => pair.Key,
                pair => new HarnessForgeChapterSnapshot(
                    pair.Value.State.ToString(),
                    pair.Value.RevisionCount,
                    pair.Value.WordCount)),
            Stats = new HarnessForgeStatsSnapshot(
                manifest.Stats.TotalInputTokens,
                manifest.Stats.TotalOutputTokens,
                manifest.Stats.AgentCalls,
                manifest.Stats.ChaptersRevised),
        };
    }
}

public static class HarnessArtifactCollector
{
    public static async Task<HarnessArtifactTrace> CaptureAsync(
        string rootPath,
        IEnumerable<string> relativePaths,
        CancellationToken ct = default)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var snapshots = new List<HarnessArtifactSnapshot>();

        foreach (var relativePath in relativePaths)
        {
            var normalizedRelativePath = relativePath.Replace('\\', '/');
            var absolutePath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelativePath));

            if (!absolutePath.StartsWith(normalizedRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Artifact path '{relativePath}' resolves outside the harness root '{normalizedRoot}'.");
            }

            if (!File.Exists(absolutePath))
            {
                snapshots.Add(new HarnessArtifactSnapshot
                {
                    RelativePath = normalizedRelativePath,
                    AbsolutePath = absolutePath,
                    Exists = false,
                });
                continue;
            }

            var info = new FileInfo(absolutePath);
            snapshots.Add(new HarnessArtifactSnapshot
            {
                RelativePath = normalizedRelativePath,
                AbsolutePath = absolutePath,
                Exists = true,
                Content = await File.ReadAllTextAsync(absolutePath, ct),
                LastModified = info.LastWriteTimeUtc,
                Length = info.Length,
            });
        }

        return new HarnessArtifactTrace
        {
            RootPath = normalizedRoot,
            Snapshots = snapshots,
        };
    }
}
