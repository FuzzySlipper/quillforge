using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Reads a file from a content directory.
/// </summary>
public sealed class ReadFileHandler : IToolHandler
{
    private readonly IContentFileService _fileService;
    private readonly ILogger<ReadFileHandler> _logger;

    public ReadFileHandler(IContentFileService fileService, ILogger<ReadFileHandler> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public string Name => "read_file";

    public ToolDefinition Definition => new(Name,
        "Read the contents of a file from a content directory (lore, story, writing, chats, forge, etc.).",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "directory": { "type": "string", "description": "Content directory name (lore, story, writing, chats, forge, etc.)" },
                    "path": { "type": "string", "description": "Relative file path within the directory" }
                },
                "required": ["directory", "path"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var dir = input.GetProperty("directory").GetString() ?? "";
        var path = input.GetProperty("path").GetString() ?? "";
        _logger.LogDebug("ReadFileHandler: reading {Dir}/{Path}", dir, path);
        var content = await _fileService.ReadAsync($"{dir}/{path}", ct);
        return ToolResult.Ok(content);
    }
}

/// <summary>
/// Writes content to a file in a content directory.
/// </summary>
public sealed class WriteFileHandler : IToolHandler
{
    private readonly IContentFileService _fileService;
    private readonly ILogger<WriteFileHandler> _logger;

    public WriteFileHandler(IContentFileService fileService, ILogger<WriteFileHandler> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public string Name => "write_file";

    public ToolDefinition Definition => new(Name,
        "Write content to a file in a content directory. Creates the file if it doesn't exist.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "directory": { "type": "string", "description": "Content directory name" },
                    "path": { "type": "string", "description": "Relative file path within the directory" },
                    "content": { "type": "string", "description": "Content to write" }
                },
                "required": ["directory", "path", "content"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var dir = input.GetProperty("directory").GetString() ?? "";
        var path = input.GetProperty("path").GetString() ?? "";
        var content = input.GetProperty("content").GetString() ?? "";
        _logger.LogDebug("WriteFileHandler: writing {Dir}/{Path} ({Length} chars)", dir, path, content.Length);
        await _fileService.WriteAsync($"{dir}/{path}", content, ct);
        return ToolResult.Ok($"Written to {dir}/{path}");
    }
}

/// <summary>
/// Lists files in a content directory.
/// </summary>
public sealed class ListFilesHandler : IToolHandler
{
    private readonly IContentFileService _fileService;
    private readonly ILogger<ListFilesHandler> _logger;

    public ListFilesHandler(IContentFileService fileService, ILogger<ListFilesHandler> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public string Name => "list_files";

    public ToolDefinition Definition => new(Name,
        "List files in a content directory. Optionally filter by pattern.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "directory": { "type": "string", "description": "Content directory name" },
                    "pattern": { "type": "string", "description": "Optional glob pattern filter (e.g. *.md)" }
                },
                "required": ["directory"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var dir = input.GetProperty("directory").GetString() ?? "";
        var pattern = input.TryGetProperty("pattern", out var p) ? p.GetString() : null;
        _logger.LogDebug("ListFilesHandler: listing {Dir} pattern={Pattern}", dir, pattern);
        var files = await _fileService.ListAsync(dir, pattern, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(files));
    }
}

/// <summary>
/// Searches file contents across a directory.
/// </summary>
public sealed class SearchFilesHandler : IToolHandler
{
    private readonly ILoreStore _loreStore;
    private readonly ILogger<SearchFilesHandler> _logger;

    public SearchFilesHandler(ILoreStore loreStore, ILogger<SearchFilesHandler> logger)
    {
        _loreStore = loreStore;
        _logger = logger;
    }

    public string Name => "search_files";

    public ToolDefinition Definition => new(Name,
        "Search for text across files in a content directory.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "directory": { "type": "string", "description": "Content directory to search" },
                    "query": { "type": "string", "description": "Text to search for" }
                },
                "required": ["directory", "query"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var dir = input.GetProperty("directory").GetString() ?? "";
        var query = input.GetProperty("query").GetString() ?? "";
        _logger.LogDebug("SearchFilesHandler: searching {Dir} for \"{Query}\"", dir, query);
        var results = await _loreStore.SearchAsync(dir, query, ct);
        var formatted = results.Select(r => new { file = r.FilePath, snippet = r.Snippet });
        return ToolResult.Ok(JsonSerializer.Serialize(formatted));
    }
}
