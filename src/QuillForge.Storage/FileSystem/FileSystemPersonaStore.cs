using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads persona/character definitions from the file system.
/// A persona can be either a single .md file or a directory containing multiple .md files
/// (which are concatenated in alphabetical order).
/// </summary>
public sealed class FileSystemPersonaStore : IPersonaStore
{
    private readonly string _personaPath;
    private readonly ILogger<FileSystemPersonaStore> _logger;

    public FileSystemPersonaStore(string personaPath, ILogger<FileSystemPersonaStore> logger)
    {
        _personaPath = personaPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string personaName, CancellationToken ct = default)
    {
        _logger.LogDebug("Loading persona: {Name}", personaName);

        // Check for a directory-based persona first
        var dirPath = Path.Combine(_personaPath, personaName);
        if (Directory.Exists(dirPath))
        {
            var files = Directory.GetFiles(dirPath, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
            {
                var parts = new List<string>();
                foreach (var file in files)
                {
                    parts.Add(await File.ReadAllTextAsync(file, ct));
                }
                _logger.LogDebug("Loaded persona {Name} from {Count} files in directory", personaName, files.Count);
                return string.Join("\n\n", parts);
            }
        }

        // Fall back to single file
        var filePath = Path.Combine(_personaPath, personaName + ".md");
        if (File.Exists(filePath))
        {
            return await File.ReadAllTextAsync(filePath, ct);
        }

        _logger.LogWarning("Persona not found: {Name}, returning empty", personaName);
        return "";
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_personaPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var personas = new HashSet<string>();

        // Add directory-based personas
        foreach (var dir in Directory.GetDirectories(_personaPath))
        {
            personas.Add(Path.GetFileName(dir));
        }

        // Add single-file personas
        foreach (var file in Directory.GetFiles(_personaPath, "*.md"))
        {
            personas.Add(Path.GetFileNameWithoutExtension(file));
        }

        var sorted = personas.OrderBy(n => n).ToList();
        return Task.FromResult<IReadOnlyList<string>>(sorted);
    }
}
