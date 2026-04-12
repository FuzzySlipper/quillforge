using QuillForge.Core.Models;

namespace QuillForge.Web.Contracts;

public sealed record ReasoningArtifactDto
{
    public required string AgentId { get; init; }
    public required string AgentLabel { get; init; }
    public required string Content { get; init; }
    public int Sequence { get; init; }
}

internal static class ReasoningContractMapper
{
    public static IReadOnlyList<ReasoningArtifactDto> ToDtos(IReadOnlyList<ReasoningArtifact>? artifacts)
    {
        if (artifacts is null || artifacts.Count == 0)
        {
            return [];
        }

        var result = new List<ReasoningArtifactDto>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            result.Add(new ReasoningArtifactDto
            {
                AgentId = artifact.AgentId,
                AgentLabel = artifact.AgentLabel,
                Content = artifact.Content,
                Sequence = artifact.Sequence,
            });
        }

        return result;
    }
}
