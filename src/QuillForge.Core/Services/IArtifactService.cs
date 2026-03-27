using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IArtifactService
{
    Artifact? GetCurrent();
    void SetCurrent(Artifact artifact);
    void ClearCurrent();
    Task<IReadOnlyList<ArtifactSummary>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(Artifact artifact, CancellationToken ct = default);
    string BuildPrompt(string userPrompt, ArtifactFormat format);
    IReadOnlyDictionary<ArtifactFormat, string> GetFormatInstructions();
}
