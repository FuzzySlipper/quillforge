namespace QuillForge.Web.Contracts;

public sealed record HealthResponse
{
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required string Build { get; init; }
    public required string Mode { get; init; }
    public required string BindMode { get; init; }
    public required string ContentRoot { get; init; }
    public required int Port { get; init; }
    public string? DesktopInstanceId { get; init; }
}
