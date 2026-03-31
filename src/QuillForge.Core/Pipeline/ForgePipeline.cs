using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Orchestrates the forge pipeline stages. Manages stage progression, manifest persistence
/// after each step, error cancellation, and restart-from-manifest resume.
/// </summary>
public sealed class ForgePipeline : IDiagnosticSource
{
    private readonly IReadOnlyList<IPipelineStage> _stages;
    private readonly IContentFileService _fileService;
    private readonly ILogger<ForgePipeline> _logger;
    private readonly TimeSpan _stageTimeout;

    private ForgeContext? _activeContext;
    private bool _pauseRequested;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ForgePipeline(
        IEnumerable<IPipelineStage> stages,
        IContentFileService fileService,
        ILogger<ForgePipeline> logger,
        TimeSpan? stageTimeout = null)
    {
        _stages = stages.OrderBy(s => s.StageEnum).ToList();
        _fileService = fileService;
        _logger = logger;
        _stageTimeout = stageTimeout ?? TimeSpan.FromHours(2);
    }

    public string Category => "forge";

    public bool IsRunning => _activeContext is not null;

    /// <summary>
    /// Runs the forge pipeline, yielding events as stages progress.
    /// Resumes from the manifest's current stage if it's past Planning.
    /// Persists the manifest after each stage completes.
    /// On error, cancels at the current point — restart will skip completed work.
    /// </summary>
    public async IAsyncEnumerable<ForgeEvent> RunAsync(
        ForgeContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _activeContext = context;
        _pauseRequested = false;

        // Auto-repair: normalize the manifest before running
        context.Manifest = ForgeManifestRepair.Normalize(context.Manifest, _logger);

        _logger.LogInformation(
            "Forge pipeline starting for project {Project}, current stage={Stage}",
            context.Manifest.ProjectName, context.Manifest.Stage);

        // Create or clear the run-specific lore file at the start of a fresh run.
        // On resume (stage > Planning), preserve accumulated run lore.
        if (!string.IsNullOrEmpty(context.RunLorePath) && context.Manifest.Stage <= ForgeStage.Planning)
        {
            await _fileService.WriteAsync(context.RunLorePath,
                "# Run Lore\n\nDetails extracted from chapters during this builder run.\n\n", ct);
            _logger.LogInformation("Initialized run lore file at {Path}", context.RunLorePath);
        }

        var startStage = context.Manifest.Stage;

        foreach (var stage in _stages)
        {
            // Skip stages already completed (resume support)
            if (stage.StageEnum < startStage)
            {
                _logger.LogDebug("Skipping completed stage {Stage}", stage.StageName);
                continue;
            }

            // Check for pause
            if (_pauseRequested)
            {
                _logger.LogInformation("Pipeline paused before stage {Stage}", stage.StageName);
                context.Manifest = context.Manifest with { Paused = true };
                await PersistManifestAsync(context, ct);
                yield break;
            }

            // Handle the writing→review→revision loop
            if (stage.StageEnum == ForgeStage.Review)
            {
                var hasRevisionsNeeded = true;
                while (hasRevisionsNeeded)
                {
                    ct.ThrowIfCancellationRequested();

                    // Run review
                    await foreach (var evt in ExecuteStageWithTimeout(stage, context, ct))
                    {
                        yield return evt;
                    }

                    // Check if any chapters need revision
                    hasRevisionsNeeded = context.Manifest.Chapters.Values
                        .Any(c => c.State == ChapterState.Revision);

                    if (hasRevisionsNeeded)
                    {
                        _logger.LogInformation("Chapters need revision, re-entering writing stage");

                        // Re-run writing for revision chapters
                        var writingStage = _stages.First(s => s.StageEnum == ForgeStage.Writing);
                        await foreach (var evt in ExecuteStageWithTimeout(writingStage, context, ct))
                        {
                            yield return evt;
                        }
                    }
                }

                // Update stage and persist
                context.Manifest = context.Manifest with
                {
                    Stage = ForgeStage.Assembly,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                await PersistManifestAsync(context, ct);
                continue;
            }

            // Normal stage execution
            await foreach (var evt in ExecuteStageWithTimeout(stage, context, ct))
            {
                yield return evt;
            }

            // Advance stage and persist
            var nextStage = stage.StageEnum + 1;
            context.Manifest = context.Manifest with
            {
                Stage = (ForgeStage)Math.Min((int)nextStage, (int)ForgeStage.Done),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await PersistManifestAsync(context, ct);

            _logger.LogInformation("Stage {Stage} completed, manifest persisted", stage.StageName);

            // Pause after chapter 1 if configured
            if (stage.StageEnum == ForgeStage.Writing && context.Manifest.PauseAfterChapter1)
            {
                var firstChapter = context.Manifest.Chapters.Keys.OrderBy(k => k).FirstOrDefault();
                if (firstChapter is not null && context.Manifest.Chapters[firstChapter].State != ChapterState.Pending)
                {
                    _logger.LogInformation("Pausing after first chapter for user review");
                    context.Manifest = context.Manifest with { Paused = true };
                    await PersistManifestAsync(context, ct);
                    yield break;
                }
            }
        }

        // Pipeline complete
        context.Manifest = context.Manifest with
        {
            Stage = ForgeStage.Done,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await PersistManifestAsync(context, ct);

        _logger.LogInformation("Forge pipeline completed for project {Project}", context.Manifest.ProjectName);
        yield return new ForgeCompletedEvent(context.Manifest.Stats);

        _activeContext = null;
    }

    /// <summary>
    /// Requests the pipeline to pause at the next stage boundary.
    /// </summary>
    public void RequestPause()
    {
        _pauseRequested = true;
        _logger.LogInformation("Pause requested for forge pipeline");
    }

    /// <summary>
    /// Rebuilds a project manifest by scanning files on disk.
    /// Used when the manifest is corrupted or missing.
    /// </summary>
    public async Task<ForgeManifest> RebuildManifestAsync(string projectName, CancellationToken ct = default)
    {
        var manifest = await ForgeManifestRepair.RebuildFromFilesAsync(projectName, _fileService, _logger, ct);
        var manifestPath = $"forge/{projectName}/manifest.json";
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await _fileService.WriteAsync(manifestPath, json, ct);
        _logger.LogInformation("Rebuilt and persisted manifest for project {Project}", projectName);
        return manifest;
    }

    private async IAsyncEnumerable<ForgeEvent> ExecuteStageWithTimeout(
        IPipelineStage stage,
        ForgeContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_stageTimeout);

        ForgeEvent? error = null;
        await using var enumerator = stage.ExecuteAsync(context, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("Stage {Stage} timed out after {Timeout}", stage.StageName, _stageTimeout);
                error = new ForgeErrorEvent($"Stage {stage.StageName} timed out", stage.StageName);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Stage {Stage} failed with error", stage.StageName);
                error = new ForgeErrorEvent(ex.Message, stage.StageName);
                break;
            }

            yield return enumerator.Current;
        }

        if (error is not null)
        {
            // Persist manifest on error so progress is saved
            await PersistManifestAsync(context, CancellationToken.None);
            yield return error;
        }
    }

    private async Task PersistManifestAsync(ForgeContext context, CancellationToken ct)
    {
        var manifestPath = $"forge/{context.Manifest.ProjectName}/manifest.json";
        var json = JsonSerializer.Serialize(context.Manifest, JsonOptions);
        await _fileService.WriteAsync(manifestPath, json, ct);
        _logger.LogDebug("Manifest persisted for project {Project}", context.Manifest.ProjectName);
    }

    public Task<IReadOnlyDictionary<string, object>> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var diag = new Dictionary<string, object>
        {
            ["is_running"] = IsRunning,
            ["pause_requested"] = _pauseRequested,
            ["stage_timeout"] = _stageTimeout.ToString(),
        };

        if (_activeContext is not null)
        {
            diag["active_project"] = _activeContext.Manifest.ProjectName;
            diag["current_stage"] = _activeContext.Manifest.Stage.ToString();
            diag["chapter_count"] = _activeContext.Manifest.ChapterCount;
        }

        return Task.FromResult<IReadOnlyDictionary<string, object>>(diag);
    }
}
