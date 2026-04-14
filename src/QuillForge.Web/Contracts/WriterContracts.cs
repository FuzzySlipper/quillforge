namespace QuillForge.Web.Contracts;

public sealed record WriterPendingMutationRequest
{
    public Guid SessionId { get; init; }
}

public sealed record WriterPendingAcceptResponse
{
    public required Guid SessionId { get; init; }
    public required string Status { get; init; }
    public required string SavedPath { get; init; }
    public required int ContentLength { get; init; }
}

public sealed record WriterPendingRejectResponse
{
    public required Guid SessionId { get; init; }
    public required string Status { get; init; }
}
