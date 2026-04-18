using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

public sealed class LoreCanonizationRuntimeState
{
    public LoreCanonizationProposalState? PendingProposal { get; set; }
}

public sealed class LoreCanonizationProposalState
{
    public required Guid SessionId { get; init; }
    public required string LoreSet { get; init; }
    public required string TargetFilePath { get; init; }
    public required string Summary { get; init; }
    public List<string> NewFacts { get; init; } = [];
    public List<string> ModifiedFacts { get; init; } = [];
    public List<string> Conflicts { get; init; } = [];
    public required string ProposedMarkdown { get; init; }
    public required string ProposedFileContent { get; init; }
    public bool CanApply { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GenerateLoreCanonizationProposalCommand(string? TargetFilePath);

public sealed record LoreCanonizationProposalGeneratedEvent
{
    public required Guid SessionId { get; init; }
    public required LoreCanonizationProposalState Proposal { get; init; }
}

public sealed record LoreCanonizationAppliedEvent
{
    public required Guid SessionId { get; init; }
    public required string LoreSet { get; init; }
    public required string TargetFilePath { get; init; }
    public required string SavedContent { get; init; }
}

public sealed record LoreCanonizationDiscardedEvent
{
    public required Guid SessionId { get; init; }
    public string? TargetFilePath { get; init; }
}
