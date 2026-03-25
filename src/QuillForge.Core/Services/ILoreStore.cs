namespace QuillForge.Core.Services;

/// <summary>
/// Access to lore corpus files for the Librarian agent.
/// </summary>
public interface ILoreStore
{
    /// <summary>
    /// Loads all lore content for a given lore set name.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default);

    /// <summary>
    /// Lists available lore set names.
    /// </summary>
    Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Searches lore content for a query string, returning matching file paths and snippets.
    /// </summary>
    Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
        string loreSetName, string query, CancellationToken ct = default);
}
