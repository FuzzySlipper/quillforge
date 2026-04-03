using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionRuntimeService : ISessionRuntimeService
{
    private const string WriterModeName = "writer";
    private const int PendingContentThreshold = 200;

    private readonly ISessionRuntimeStore _store;
    private readonly ISessionMutationGate _gate;
    private readonly HashSet<string> _knownModes;
    private readonly ILogger<SessionRuntimeService> _logger;

    public SessionRuntimeService(
        ISessionRuntimeStore store,
        ISessionMutationGate gate,
        IEnumerable<IMode> modes,
        ILogger<SessionRuntimeService> logger)
    {
        _store = store;
        _gate = gate;
        _logger = logger;

        _knownModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in modes)
        {
            _knownModes.Add(mode.Name);
        }
    }

    public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        return _store.LoadAsync(sessionId, ct);
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> SetModeAsync(
        Guid? sessionId,
        SetSessionModeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_mode";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        if (!_knownModes.Contains(command.Mode))
        {
            _logger.LogWarning(
                "Session mode update rejected: session={SessionId} invalidMode={Mode}",
                sessionId,
                command.Mode);
            return SessionMutationResult<SessionRuntimeState>.Invalid($"Unknown mode: {command.Mode}");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var oldMode = state.Mode.ActiveModeName;

        if (string.Equals(oldMode, WriterModeName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.Mode, WriterModeName, StringComparison.OrdinalIgnoreCase))
        {
            state.Writer.PendingContent = null;
            state.Writer.State = WriterState.Idle;
            _logger.LogInformation("Writer pending state reset during mode change for session {SessionId}", sessionId);
        }

        state.Mode.ActiveModeName = command.Mode;
        state.Mode.ProjectName = command.Project;
        state.Mode.CurrentFile = command.File;
        state.Mode.Character = command.Character;

        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Session mode updated: session={SessionId} oldMode={OldMode} newMode={NewMode} project={Project} file={File} character={Character}",
            sessionId,
            oldMode,
            state.Mode.ActiveModeName,
            state.Mode.ProjectName,
            state.Mode.CurrentFile,
            state.Mode.Character);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "capture_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        if (!string.Equals(state.Mode.ActiveModeName, WriterModeName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} currentMode={CurrentMode} sourceMode={SourceMode}",
                sessionId,
                state.Mode.ActiveModeName,
                command.SourceMode);
            return SessionMutationResult<SessionRuntimeState>.Success(state);
        }

        if (state.Writer.State != WriterState.Idle)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} writerState={WriterState}",
                sessionId,
                state.Writer.State);
            return SessionMutationResult<SessionRuntimeState>.Success(state);
        }

        if (string.IsNullOrWhiteSpace(command.Content) || command.Content.Length <= PendingContentThreshold)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} contentLength={Length}",
                sessionId,
                command.Content.Length);
            return SessionMutationResult<SessionRuntimeState>.Success(state);
        }

        state.Writer.PendingContent = command.Content;
        state.Writer.State = WriterState.PendingReview;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content captured: session={SessionId} contentLength={Length}",
            sessionId,
            command.Content.Length);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }

    public async Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "accept_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<WriterPendingDecisionResult>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview || state.Writer.PendingContent is null)
        {
            _logger.LogWarning("Writer pending accept rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<WriterPendingDecisionResult>.Invalid("No pending writer content to accept.");
        }

        var accepted = state.Writer.PendingContent;
        state.Writer.PendingContent = null;
        state.Writer.State = WriterState.Idle;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content accepted: session={SessionId} contentLength={Length}",
            sessionId,
            accepted.Length);

        return SessionMutationResult<WriterPendingDecisionResult>.Success(
            new WriterPendingDecisionResult(accepted));
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> RejectWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "reject_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview)
        {
            _logger.LogWarning("Writer pending reject rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<SessionRuntimeState>.Invalid("No pending writer content to reject.");
        }

        state.Writer.PendingContent = null;
        state.Writer.State = WriterState.Idle;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("Writer pending content rejected: session={SessionId}", sessionId);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> UpdateNarrativeStateAsync(
        Guid? sessionId,
        UpdateNarrativeStateCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "update_narrative_state";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        if (string.IsNullOrWhiteSpace(command.DirectorNotes))
        {
            _logger.LogWarning(
                "Narrative state update rejected: session={SessionId} empty director notes",
                sessionId);
            return SessionMutationResult<SessionRuntimeState>.Invalid("Director notes are required.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        state.Narrative.DirectorNotes = command.DirectorNotes;
        if (command.ActivePlotFile is not null)
        {
            state.Narrative.ActivePlotFile = command.ActivePlotFile;
        }
        if (command.PlotProgress is not null)
        {
            state.Narrative.PlotProgress.CurrentBeat = command.PlotProgress.CurrentBeat;
            state.Narrative.PlotProgress.CompletedBeats = command.PlotProgress.CompletedBeats?.ToList() ?? [];
            state.Narrative.PlotProgress.Deviations = command.PlotProgress.Deviations?.ToList() ?? [];
        }

        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Narrative state updated: session={SessionId} notesLength={Length} activePlot={ActivePlot}",
            sessionId,
            command.DirectorNotes.Length,
            state.Narrative.ActivePlotFile);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> SetActivePlotAsync(
        Guid? sessionId,
        SetActivePlotCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_active_plot";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        if (string.IsNullOrWhiteSpace(command.PlotFileName))
        {
            _logger.LogWarning("Active plot update rejected: session={SessionId} empty plot file", sessionId);
            return SessionMutationResult<SessionRuntimeState>.Invalid("Plot file name is required.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        state.Narrative.ActivePlotFile = command.PlotFileName;
        state.Narrative.PlotProgress = new PlotProgressState();
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Active plot set: session={SessionId} plot={Plot}",
            sessionId,
            state.Narrative.ActivePlotFile);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }

    public async Task<SessionMutationResult<SessionRuntimeState>> ClearActivePlotAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "clear_active_plot";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionRuntimeState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        state.Narrative.ActivePlotFile = null;
        state.Narrative.PlotProgress = new PlotProgressState();
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("Active plot cleared: session={SessionId}", sessionId);

        return SessionMutationResult<SessionRuntimeState>.Success(state);
    }
}
