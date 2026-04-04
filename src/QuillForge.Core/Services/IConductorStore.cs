namespace QuillForge.Core.Services;

public interface IConductorStore
{
    Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
