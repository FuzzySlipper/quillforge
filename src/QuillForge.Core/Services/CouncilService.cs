using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class CouncilService : ICouncilService
{
    private readonly IContentFileService _fileService;
    private readonly DelegatePool _pool;
    private readonly ILogger<CouncilService> _logger;

    public CouncilService(IContentFileService fileService, DelegatePool pool, ILogger<CouncilService> logger)
    {
        _fileService = fileService;
        _pool = pool;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CouncilMember>> LoadMembersAsync(CancellationToken ct = default)
    {
        var files = await _fileService.ListAsync("council", "*.md", ct);
        var members = new List<CouncilMember>();

        foreach (var file in files)
        {
            try
            {
                var content = await _fileService.ReadAsync(file, ct);
                var member = ParseMemberFile(Path.GetFileNameWithoutExtension(file.Split('/').Last()), content);
                members.Add(member);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse council member {File}", file);
            }
        }

        return members;
    }

    public async Task<CouncilResult> RunCouncilAsync(string query, CancellationToken ct = default)
    {
        var members = await LoadMembersAsync(ct);
        if (members.Count == 0)
        {
            return new CouncilResult { Query = query, Members = [] };
        }

        var tasks = members.Select(m => new DelegateTask
        {
            Id = m.Name,
            SystemPrompt = m.SystemPrompt,
            UserPrompt = query,
            ProviderAlias = m.ProviderAlias ?? "default",
            ModelOverride = m.Model,
            Temperature = 0.7f,
            MaxTokens = 1024,
        });

        _logger.LogInformation("Running council with {Count} members", members.Count);
        var results = await _pool.RunAsync(tasks, ct: ct);

        var responses = members.Select(m =>
        {
            var result = results.GetValueOrDefault(m.Name);
            return new CouncilMemberResponse
            {
                Name = m.Name,
                Model = result?.Model ?? m.Model ?? "unknown",
                ProviderAlias = result?.ProviderAlias ?? m.ProviderAlias ?? "default",
                Content = result?.Content ?? "",
                Error = result?.Error,
            };
        }).ToList();

        return new CouncilResult { Query = query, Members = responses };
    }

    public string FormatForOrchestrator(CouncilResult result)
    {
        var parts = new List<string>
        {
            $"The user asked for a council review of the following query:\n\n\"{result.Query}\"\n",
            $"{result.Members.Count} council members responded. " +
            "Please analyze their responses and write a synthesis report that identifies:\n" +
            "1. Points of agreement across members\n" +
            "2. Points of disagreement or tension\n" +
            "3. Unique insights from individual members\n" +
            "4. Your overall assessment incorporating their perspectives\n\n" +
            "End with a section containing each member's full response for reference.\n",
        };

        foreach (var member in result.Members)
        {
            parts.Add($"--- {member.Name.ToUpperInvariant()} ({member.Model}) ---");
            parts.Add(member.Error is not null ? $"[ERROR: {member.Error}]" : member.Content);
            parts.Add("");
        }

        return string.Join("\n", parts);
    }

    private static CouncilMember ParseMemberFile(string name, string content)
    {
        var lines = content.Split('\n');
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var promptStart = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var stripped = lines[i].Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                promptStart = i + 1;
                break;
            }

            var colonIndex = stripped.IndexOf(':');
            if (colonIndex > 0
                && !stripped.StartsWith("You", StringComparison.OrdinalIgnoreCase)
                && !stripped.StartsWith('#'))
            {
                var key = stripped[..colonIndex].Trim();
                var value = stripped[(colonIndex + 1)..].Trim();
                config[key] = value;
            }
            else
            {
                promptStart = i;
                break;
            }
        }

        var systemPrompt = string.Join("\n", lines.Skip(promptStart)).Trim();

        return new CouncilMember
        {
            Name = name,
            Model = config.GetValueOrDefault("model"),
            ProviderAlias = config.GetValueOrDefault("provider"),
            BaseUrl = config.GetValueOrDefault("base_url"),
            SystemPrompt = systemPrompt,
        };
    }
}
