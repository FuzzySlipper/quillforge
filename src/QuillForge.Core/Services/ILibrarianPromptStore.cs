namespace QuillForge.Core.Services;

/// <summary>
/// Loads named librarian instruction prompts from the file system.
/// Each prompt is a markdown file containing user-customizable instructions
/// for how the Librarian agent should interpret and filter lore queries.
/// </summary>
public interface ILibrarianPromptStore
{
    Task<string> LoadAsync(string promptName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
