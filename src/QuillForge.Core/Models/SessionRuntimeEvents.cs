namespace QuillForge.Core.Models;

/// <summary>
/// Base event for the writer-pending capture flow. Derived facts describe whether
/// QuillForge captured pending prose or explicitly skipped capture.
/// </summary>
public abstract record WriterPendingCaptureEvent;

public sealed record WriterPendingContentCapturedEvent(
    SessionState SessionView,
    int ContentLength,
    string SourceMode) : WriterPendingCaptureEvent;

public sealed record WriterPendingCaptureSkippedEvent(
    SessionState SessionView,
    string ReasonCode) : WriterPendingCaptureEvent;

public sealed record WriterPendingContentAcceptedEvent(
    Guid? SessionId,
    string AcceptedContent);

public sealed record WriterPendingContentRejectedEvent(
    SessionState SessionView);
