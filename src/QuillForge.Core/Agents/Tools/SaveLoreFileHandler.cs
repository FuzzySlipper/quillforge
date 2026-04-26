using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// App-owned lore file writer for Lore Builder mode. General write_file access
/// intentionally cannot write lore; this tool keeps lore writes constrained to
/// the active lore set and requires an explicit user directive/confirmation.
/// </summary>
public sealed class SaveLoreFileHandler : TypedToolHandler<SaveLoreFileArgs>
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IContentFileService _fileService;
    private readonly ILogger<SaveLoreFileHandler> _logger;

    public SaveLoreFileHandler(
        IContentFileService fileService,
        ILogger<SaveLoreFileHandler> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public override string Name => "save_lore_file";

    public override ToolDefinition Definition => new(Name,
        "Create or update a markdown file in the active lore set. Use only in Lore Builder mode after the user has explicitly requested or approved saving this lore.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "target_file_path": {
                        "type": "string",
                        "description": "Relative markdown path inside the active lore set, such as factions/silverwatch.md"
                    },
                    "content": {
                        "type": "string",
                        "description": "Markdown content to save"
                    },
                    "operation": {
                        "type": "string",
                        "enum": ["replace", "append"],
                        "description": "replace overwrites the file; append adds the content to the end. Defaults to replace."
                    },
                    "user_confirmed": {
                        "type": "boolean",
                        "description": "True only when the user explicitly asked for or approved saving this lore."
                    },
                    "confirmation_note": {
                        "type": "string",
                        "description": "Short note describing the user's directive or approval"
                    }
                },
                "required": ["target_file_path", "content", "user_confirmed"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(SaveLoreFileArgs input, AgentContext context, CancellationToken ct = default)
    {
        if (context.ActiveMode != Mode.Lore)
        {
            return ToolResult.Fail("save_lore_file is only available in Lore Builder mode.");
        }

        if (!input.UserConfirmed)
        {
            return ToolResult.Fail("The user must explicitly request or approve saving this lore before save_lore_file can write.");
        }

        if (string.IsNullOrWhiteSpace(context.ActiveLoreSet))
        {
            return ToolResult.Fail("No active lore set is selected.");
        }

        if (string.IsNullOrWhiteSpace(input.Content))
        {
            return ToolResult.Fail("content is required.");
        }

        string targetFilePath;
        string operation;
        try
        {
            targetFilePath = NormalizeTargetFilePath(input.TargetFilePath);
            operation = NormalizeOperation(input.Operation);
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        var relativePath = $"{ContentPaths.Lore}/{context.ActiveLoreSet}/{targetFilePath}";
        var contentToSave = NormalizeContent(input.Content);
        var existed = await _fileService.ExistsAsync(relativePath, ct);

        if (operation == "append" && existed)
        {
            var existing = await _fileService.ReadAsync(relativePath, ct);
            contentToSave = AppendContent(existing, contentToSave);
        }

        await _fileService.WriteAsync(relativePath, contentToSave, ct);

        _logger.LogInformation(
            "Saved lore file through Lore Builder: loreSet={LoreSet} file={File} operation={Operation} confirmed={Confirmation}",
            context.ActiveLoreSet,
            targetFilePath,
            operation,
            input.ConfirmationNote);

        var payload = new
        {
            status = "saved",
            lore_set = context.ActiveLoreSet,
            target_file_path = targetFilePath,
            operation,
            existed,
            character_count = contentToSave.Length,
        };

        return ToolResult.Ok(JsonSerializer.Serialize(payload, s_jsonOptions));
    }

    private static string NormalizeTargetFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("target_file_path is required.");
        }

        var normalized = value.Replace('\\', '/').Trim().TrimStart('/');
        if (Path.IsPathRooted(normalized) || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new JsonException("target_file_path must be relative and cannot traverse directories.");
        }

        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(normalized)))
        {
            throw new JsonException("target_file_path must name a markdown file.");
        }

        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            normalized += ".md";
        }
        else if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("target_file_path must use the .md extension.");
        }

        return normalized;
    }

    private static string NormalizeOperation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "replace";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "replace" or "append"
            ? normalized
            : throw new JsonException("operation must be either replace or append.");
    }

    private static string NormalizeContent(string content)
    {
        return content.ReplaceLineEndings("\n").TrimEnd() + "\n";
    }

    private static string AppendContent(string existing, string addition)
    {
        var normalizedExisting = existing.ReplaceLineEndings("\n").TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            return addition;
        }

        return normalizedExisting + "\n\n" + addition;
    }
}

public sealed record SaveLoreFileArgs
{
    public string TargetFilePath { get; init; } = "";
    public string Content { get; init; } = "";
    public string? Operation { get; init; }
    public bool UserConfirmed { get; init; }
    public string? ConfirmationNote { get; init; }
}
