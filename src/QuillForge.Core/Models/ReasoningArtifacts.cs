namespace QuillForge.Core.Models;

/// <summary>
/// A single reasoning artifact attached to a persisted assistant-visible turn.
/// These are QuillForge-owned display/diagnostic models, not provider replay.
/// </summary>
public sealed record ReasoningArtifact
{
    public required string AgentId { get; init; }
    public required string AgentLabel { get; init; }
    public required string Content { get; init; }
    public int Sequence { get; init; }
}

/// <summary>
/// Helper methods for creating and selecting reasoning artifacts.
/// </summary>
public static class ReasoningArtifacts
{
    public static ReasoningArtifact? CreateForAgent(
        string? agentId,
        string? reasoning,
        ProviderReplayEnvelope? providerReplay = null,
        int sequence = 0)
    {
        var normalizedAgentId = NormalizeAgentId(agentId);
        if (normalizedAgentId is null)
        {
            return null;
        }

        var content = GetContent(reasoning, providerReplay);
        if (content is null)
        {
            return null;
        }

        return new ReasoningArtifact
        {
            AgentId = normalizedAgentId,
            AgentLabel = GetAgentLabel(normalizedAgentId),
            Content = content,
            Sequence = sequence,
        };
    }

    public static string? GetContent(string? reasoning, ProviderReplayEnvelope? providerReplay = null)
    {
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            return reasoning;
        }

        return providerReplay is ReasoningReplayEnvelope reasoningReplay
            && !string.IsNullOrWhiteSpace(reasoningReplay.ReasoningContent)
                ? reasoningReplay.ReasoningContent
                : null;
    }

    public static ReasoningArtifact? SelectDefault(IReadOnlyList<ReasoningArtifact>? artifacts)
    {
        if (artifacts is null || artifacts.Count == 0)
        {
            return null;
        }

        for (var index = artifacts.Count - 1; index >= 0; index--)
        {
            if (string.Equals(artifacts[index].AgentId, "prose-writer", StringComparison.OrdinalIgnoreCase))
            {
                return artifacts[index];
            }
        }

        for (var index = artifacts.Count - 1; index >= 0; index--)
        {
            if (string.Equals(artifacts[index].AgentId, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                return artifacts[index];
            }
        }

        ReasoningArtifact? selected = null;
        foreach (var artifact in artifacts)
        {
            if (selected is null || artifact.Sequence >= selected.Sequence)
            {
                selected = artifact;
            }
        }

        return selected;
    }

    public static string? GetDefaultReasoning(
        IReadOnlyList<ReasoningArtifact>? artifacts,
        string? fallbackReasoning = null)
    {
        var selected = SelectDefault(artifacts);
        return selected?.Content ?? fallbackReasoning;
    }

    public static string GetAgentLabel(string agentId) => agentId switch
    {
        "orchestrator" => "Orchestrator",
        "narrative-director" => "Narrative Director",
        "prose-writer" => "Prose Writer",
        "assistant" => "Assistant",
        "research" => "Research",
        "forge-writer" => "Forge Writer",
        "forge-planner" => "Forge Planner",
        "librarian" => "Librarian",
        _ => TitleCaseAgentId(agentId),
    };

    private static string? NormalizeAgentId(string? agentId)
    {
        return string.IsNullOrWhiteSpace(agentId) ? null : agentId.Trim();
    }

    private static string TitleCaseAgentId(string agentId)
    {
        var parts = agentId
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "Assistant";
        }

        var titled = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Length == 1)
            {
                titled.Add(part.ToUpperInvariant());
                continue;
            }

            titled.Add(char.ToUpperInvariant(part[0]) + part[1..]);
        }

        return titled.Count == 0 ? "Assistant" : string.Join(" ", titled);
    }
}

/// <summary>
/// Per-request collector for reasoning artifacts emitted by nested or top-level agents.
/// </summary>
public sealed class ReasoningArtifactCollector
{
    private readonly object _gate = new();
    private readonly List<ReasoningArtifact> _artifacts = [];

    public Task CaptureAsync(ReasoningArtifact artifact, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _artifacts.Add(artifact with
            {
                Sequence = _artifacts.Count,
            });
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<ReasoningArtifact> Snapshot()
    {
        lock (_gate)
        {
            return [.. _artifacts];
        }
    }

    public string? GetDefaultReasoning(string? fallbackReasoning = null)
    {
        return ReasoningArtifacts.GetDefaultReasoning(Snapshot(), fallbackReasoning);
    }
}
