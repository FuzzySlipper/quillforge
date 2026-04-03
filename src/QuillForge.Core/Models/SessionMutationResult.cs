namespace QuillForge.Core.Models;

public enum SessionMutationStatus
{
    Success,
    Busy,
    Invalid,
}

public sealed record SessionMutationResult<T>
{
    public required SessionMutationStatus Status { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }

    public static SessionMutationResult<T> Success(T value) => new()
    {
        Status = SessionMutationStatus.Success,
        Value = value,
    };

    public static SessionMutationResult<T> Busy(string message) => new()
    {
        Status = SessionMutationStatus.Busy,
        Error = message,
    };

    public static SessionMutationResult<T> Invalid(string message) => new()
    {
        Status = SessionMutationStatus.Invalid,
        Error = message,
    };
}

public sealed record WriterPendingDecisionResult(string AcceptedContent);
