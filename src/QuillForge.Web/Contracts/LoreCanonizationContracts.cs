namespace QuillForge.Web.Contracts;

public sealed record LoreCanonizationPreviewRequest
{
    public Guid SessionId { get; init; }
    public string? TargetFilePath { get; init; }
}

public sealed record LoreCanonizationMutationRequest
{
    public Guid SessionId { get; init; }
}

public sealed record LoreCanonizationProposalDto
{
    public required Guid SessionId { get; init; }
    public required string LoreSet { get; init; }
    public required string TargetFilePath { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> NewFacts { get; init; }
    public required IReadOnlyList<string> ModifiedFacts { get; init; }
    public required IReadOnlyList<string> Conflicts { get; init; }
    public required string ProposedMarkdown { get; init; }
    public required string ProposedFileContent { get; init; }
    public required bool CanApply { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}

public sealed record LoreCanonizationPreviewResponse
{
    public required Guid SessionId { get; init; }
    public required string Status { get; init; }
    public required LoreCanonizationProposalDto Proposal { get; init; }
}

public sealed record LoreCanonizationApplyResponse
{
    public required Guid SessionId { get; init; }
    public required string Status { get; init; }
    public required string LoreSet { get; init; }
    public required string TargetFilePath { get; init; }
    public required int ContentLength { get; init; }
}

public sealed record LoreCanonizationDiscardResponse
{
    public required Guid SessionId { get; init; }
    public required string Status { get; init; }
    public string? TargetFilePath { get; init; }
}
