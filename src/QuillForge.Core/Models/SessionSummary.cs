namespace QuillForge.Core.Models;

/// <summary>
/// Lightweight summary of a session for listing purposes.
/// </summary>
public sealed record SessionSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required int MessageCount { get; init; }
    public Mode? LastMode { get; init; }
}
