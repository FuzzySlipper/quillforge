using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Creates formal code change request files for the developer to review.
/// Stores them as markdown in the code-requests directory.
/// </summary>
public sealed class RequestCodeChangeHandler : IToolHandler
{
    private readonly IContentFileService _fileService;
    private readonly ILogger<RequestCodeChangeHandler> _logger;

    public RequestCodeChangeHandler(IContentFileService fileService, ILogger<RequestCodeChangeHandler> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public string Name => "request_code_change";

    public ToolDefinition Definition => new(Name,
        "Create a formal code change request document for the developer to review and implement.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "title": {
                        "type": "string",
                        "description": "Short title for the change request"
                    },
                    "description": {
                        "type": "string",
                        "description": "Detailed description of what needs to change and why"
                    },
                    "files_affected": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "List of file paths that would need to be modified"
                    },
                    "changes": {
                        "type": "string",
                        "description": "Specific code changes or pseudocode showing what to do"
                    },
                    "rationale": {
                        "type": "string",
                        "description": "Why this change is needed"
                    }
                },
                "required": ["title", "description"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var title = input.GetProperty("title").GetString() ?? "Untitled";
        var description = input.GetProperty("description").GetString() ?? "";
        var rationale = input.TryGetProperty("rationale", out var rat) ? rat.GetString() ?? "" : "";
        var changes = input.TryGetProperty("changes", out var ch) ? ch.GetString() ?? "" : "";

        var filesAffected = new List<string>();
        if (input.TryGetProperty("files_affected", out var files))
        {
            foreach (var f in files.EnumerateArray())
            {
                var val = f.GetString();
                if (val is not null) filesAffected.Add(val);
            }
        }

        // Build the slug and filename
        var slug = new string(title.ToLowerInvariant()
            .Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());
        if (slug.Length > 50) slug = slug[..50];
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"docs/code-requests/{timestamp}-{slug}.md";

        // Build markdown content — use concatenation to avoid indentation leaking into output
        var md = $"# Code Change Request: {title}\n"
            + "\n"
            + $"**Date:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n"
            + $"**Session:** {context.SessionId}\n"
            + $"**Mode:** {context.ActiveMode}\n"
            + "\n"
            + "## Description\n"
            + "\n"
            + description + "\n";

        if (!string.IsNullOrWhiteSpace(rationale))
        {
            md += "\n## Rationale\n\n" + rationale + "\n";
        }

        if (filesAffected.Count > 0)
        {
            md += "\n## Files Affected\n\n";
            foreach (var file in filesAffected)
            {
                md += $"- `{file}`\n";
            }
        }

        if (!string.IsNullOrWhiteSpace(changes))
        {
            md += "\n## Changes\n\n" + changes + "\n";
        }

        await _fileService.WriteAsync(fileName, md, ct);
        _logger.LogInformation("Code change request created: {FileName}", fileName);

        return ToolResult.Ok($"Code change request saved to {fileName}");
    }
}
