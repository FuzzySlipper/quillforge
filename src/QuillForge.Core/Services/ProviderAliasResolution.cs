namespace QuillForge.Core.Services;

/// <summary>
/// Result of resolving a user/configured provider alias to a registered provider alias.
/// </summary>
public sealed record ProviderAliasResolution
{
    public required string RequestedAlias { get; init; }
    public string? ResolvedAlias { get; init; }
    public string? Error { get; init; }

    public bool IsResolved => Error is null && !string.IsNullOrWhiteSpace(ResolvedAlias);

    public static ProviderAliasResolution Resolved(string requestedAlias, string resolvedAlias) => new()
    {
        RequestedAlias = requestedAlias,
        ResolvedAlias = resolvedAlias,
    };

    public static ProviderAliasResolution Failed(string requestedAlias, string error) => new()
    {
        RequestedAlias = requestedAlias,
        Error = error,
    };
}
