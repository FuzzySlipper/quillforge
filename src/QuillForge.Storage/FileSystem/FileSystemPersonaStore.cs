using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads persona/character definitions from the file system.
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
        var path = FindPersonaFile(personaName);
        _logger.LogDebug("Loading persona: {Path}", path);

        if (!File.Exists(path))
        {
            _logger.LogWarning("Persona not found: {Name}, returning empty", personaName);
            return "";
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_personaPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var personas = Directory.GetFiles(_personaPath, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(personas);
    }

    private string FindPersonaFile(string personaName)
    {
        var exact = Path.Combine(_personaPath, personaName);
        if (File.Exists(exact)) return exact;

        return Path.Combine(_personaPath, personaName + ".md");
    }
}
