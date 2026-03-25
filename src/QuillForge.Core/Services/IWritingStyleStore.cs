namespace QuillForge.Core.Services;

/// <summary>
/// Access to writing style templates.
/// </summary>
public interface IWritingStyleStore
{
    Task<string> LoadAsync(string styleName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
