namespace QuillForge.Web.Contracts;

public sealed record AppSettingsResponse
{
    public required WebSearchSettingsResponse WebSearch { get; init; }
}

public sealed record WebSearchSettingsResponse
{
    public required bool Enabled { get; init; }
    public required string Provider { get; init; }
    public string? SearxngUrl { get; init; }
    public required bool TavilyApiKeySet { get; init; }
    public required bool BraveApiKeySet { get; init; }
    public required bool GoogleApiKeySet { get; init; }
    public string? GoogleCxId { get; init; }
    public required bool ZaiApiKeySet { get; init; }
    public string? ZaiMcpEndpoint { get; init; }
    public string? ZaiMcpToolName { get; init; }
    public required int MaxResults { get; init; }
    public required IReadOnlyList<string> SupportedProviders { get; init; }
}

public sealed record WebSearchSettingsUpdateRequest
{
    public bool? Enabled { get; init; }
    public string? Provider { get; init; }
    public string? SearxngUrl { get; init; }
    public string? TavilyApiKey { get; init; }
    public bool ClearTavilyApiKey { get; init; }
    public string? BraveApiKey { get; init; }
    public bool ClearBraveApiKey { get; init; }
    public string? GoogleApiKey { get; init; }
    public bool ClearGoogleApiKey { get; init; }
    public string? GoogleCxId { get; init; }
    public string? ZaiApiKey { get; init; }
    public bool ClearZaiApiKey { get; init; }
    public string? ZaiMcpEndpoint { get; init; }
    public string? ZaiMcpToolName { get; init; }
    public int? MaxResults { get; init; }
}
