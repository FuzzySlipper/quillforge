namespace QuillForge.Core.Models;

public sealed record SetSessionModeCommand(
    string Mode,
    string? Project,
    string? File,
    string? Character);

public sealed record SetSessionProfileCommand(
    string? ProfileId,
    string? Conductor,
    string? LoreSet,
    string? NarrativeRules,
    string? WritingStyle);

public sealed record CaptureWriterPendingCommand(
    string Content,
    string SourceMode);

public sealed record UpdateNarrativeStateCommand(
    string DirectorNotes,
    string? ActivePlotFile = null,
    PlotProgressUpdate? PlotProgress = null);

public sealed record PlotProgressUpdate(
    string? CurrentBeat,
    IReadOnlyList<string>? CompletedBeats = null,
    IReadOnlyList<string>? Deviations = null);

public sealed record SetActivePlotCommand(string PlotFileName);
