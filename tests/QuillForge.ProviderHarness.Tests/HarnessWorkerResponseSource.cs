using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuillForge.ProviderHarness.Tests;

public enum HarnessWorkerRole
{
    Planner,
    Writer,
    Reviewer,
    Librarian,
    GeneralChat,
}

public sealed record HarnessWorkerRoute(
    string Model,
    HarnessWorkerRole Role);

public sealed record HarnessWorkerScenario
{
    public string Name { get; init; } = "worker-backed-harness";
    public string ProjectName { get; init; } = "exploratory-project";
    public string Premise { get; init; } = "";
    public IReadOnlyList<HarnessWorkerRoute> Routes { get; init; } = [];
    public IReadOnlyDictionary<string, string> LoreFiles { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record HarnessWorkerTrace
{
    public required string Role { get; init; }
    public required string Strategy { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public string? RequestSummary { get; init; }
    public string? Notes { get; init; }
    public int ProposedToolCallCount { get; init; }
    public string? OutputPreview { get; init; }
}

public sealed record HarnessWorkerResponse
{
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
    public IReadOnlyList<HarnessToolCallPlan> ToolCalls { get; init; } = [];
    public string FinishReason { get; init; } = "stop";
    public HarnessUsage? Usage { get; init; }
    public string? Notes { get; init; }
}

public interface IHarnessProviderWorker
{
    HarnessWorkerRole Role { get; }

    Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct);
}

public sealed class WorkerBackedHarnessResponseSource : IHarnessResponseSource
{
    private readonly HarnessWorkerScenario _scenario;
    private readonly IReadOnlyDictionary<string, HarnessWorkerRole> _routes;
    private readonly IReadOnlyDictionary<HarnessWorkerRole, IHarnessProviderWorker> _workers;

    public WorkerBackedHarnessResponseSource(
        HarnessWorkerScenario scenario,
        IEnumerable<IHarnessProviderWorker>? workers = null)
    {
        _scenario = scenario;
        _routes = scenario.Routes.ToDictionary(
            route => route.Model,
            route => route.Role,
            StringComparer.Ordinal);

        var configuredWorkers = new Dictionary<HarnessWorkerRole, IHarnessProviderWorker>();
        if (workers is not null)
        {
            foreach (var worker in workers)
            {
                configuredWorkers[worker.Role] = worker;
            }
        }

        EnsureDefaultWorker(configuredWorkers, new PrototypePlannerHarnessWorker());
        EnsureDefaultWorker(configuredWorkers, new PrototypeWriterHarnessWorker());
        EnsureDefaultWorker(configuredWorkers, new PrototypeReviewerHarnessWorker());
        EnsureDefaultWorker(configuredWorkers, new PrototypeLibrarianHarnessWorker());
        EnsureDefaultWorker(configuredWorkers, new PrototypeGeneralChatHarnessWorker());

        _workers = configuredWorkers;
    }

    public string ScenarioName => _scenario.Name;

    public IReadOnlyList<string> Models => _routes.Keys.OrderBy(model => model, StringComparer.Ordinal).ToList();

    public async Task<HarnessResponsePlan> GetNextResponseAsync(HarnessObservedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new InvalidOperationException(
                $"Worker-backed harness scenario '{ScenarioName}' received a request without a model.");
        }

        if (!_routes.TryGetValue(request.Model, out var role))
        {
            throw new InvalidOperationException(
                $"Worker-backed harness scenario '{ScenarioName}' has no worker route for model '{request.Model}'.");
        }

        if (!_workers.TryGetValue(role, out var worker))
        {
            throw new InvalidOperationException(
                $"Worker-backed harness scenario '{ScenarioName}' has no worker registered for role '{role}'.");
        }

        var parsedRequest = HarnessWorkerRequest.Parse(request);
        var startedAt = DateTimeOffset.UtcNow;
        var response = await worker.GenerateAsync(parsedRequest, _scenario, ct);
        var completedAt = DateTimeOffset.UtcNow;

        var usage = response.Usage ?? EstimateUsage(parsedRequest, response);
        var workerTrace = new HarnessWorkerTrace
        {
            Role = role.ToString(),
            Strategy = "prototype-role-worker",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            RequestSummary = parsedRequest.BuildSummary(),
            Notes = response.Notes,
            ProposedToolCallCount = response.ToolCalls.Count,
            OutputPreview = CreatePreview(response),
        };

        if (request.Stream)
        {
            return new HarnessResponsePlan
            {
                Mode = HarnessResponseMode.ScriptedStream,
                ExpectedModel = request.Model,
                StreamEvents = BuildStreamEvents(response, usage),
                Usage = usage,
                FinishReason = response.FinishReason,
                WorkerTrace = workerTrace,
            };
        }

        return new HarnessResponsePlan
        {
            Mode = HarnessResponseMode.ScriptedComplete,
            ExpectedModel = request.Model,
            Message = new HarnessAssistantMessage
            {
                Content = response.Content,
                ReasoningContent = response.ReasoningContent,
                ToolCalls = response.ToolCalls,
            },
            Usage = usage,
            FinishReason = response.FinishReason,
            WorkerTrace = workerTrace,
        };
    }

    private static void EnsureDefaultWorker(
        IDictionary<HarnessWorkerRole, IHarnessProviderWorker> workers,
        IHarnessProviderWorker worker)
    {
        if (!workers.ContainsKey(worker.Role))
        {
            workers[worker.Role] = worker;
        }
    }

    private static HarnessUsage EstimateUsage(HarnessWorkerRequest request, HarnessWorkerResponse response)
    {
        var promptTokens = Math.Max(12, request.ObservedRequest.RawBody.Length / 5);
        var completionLength =
            (response.Content?.Length ?? 0) +
            (response.ReasoningContent?.Length ?? 0) +
            response.ToolCalls.Sum(toolCall => toolCall.ArgumentsJson.Length + toolCall.Name.Length);
        var completionTokens = Math.Max(8, completionLength / 5);
        return new HarnessUsage(promptTokens, completionTokens);
    }

    private static IReadOnlyList<HarnessStreamEventPlan> BuildStreamEvents(
        HarnessWorkerResponse response,
        HarnessUsage usage)
    {
        return
        [
            new HarnessStreamEventPlan
            {
                TextDelta = response.Content,
                ReasoningDelta = response.ReasoningContent,
                ToolCalls = response.ToolCalls
                    .Select((toolCall, index) => new HarnessToolCallDeltaPlan(
                        index,
                        toolCall.Id,
                        toolCall.Name,
                        toolCall.ArgumentsJson))
                    .ToList(),
                FinishReason = response.FinishReason,
                Usage = usage,
            },
        ];
    }

    private static string? CreatePreview(HarnessWorkerResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            return Truncate(response.Content);
        }

        if (response.ToolCalls.Count > 0)
        {
            var toolNames = string.Join(", ", response.ToolCalls.Select(toolCall => toolCall.Name));
            return Truncate($"tool calls: {toolNames}");
        }

        if (!string.IsNullOrWhiteSpace(response.ReasoningContent))
        {
            return Truncate(response.ReasoningContent);
        }

        return null;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? "";
        }

        return value.Length <= 160 ? value : value[..160] + "...";
    }
}

public sealed class HarnessWorkerRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private HarnessWorkerRequest(
        HarnessObservedRequest observedRequest,
        IReadOnlyList<HarnessWorkerMessage> messages,
        IReadOnlyList<string> availableTools)
    {
        ObservedRequest = observedRequest;
        Messages = messages;
        AvailableTools = availableTools;
    }

    public HarnessObservedRequest ObservedRequest { get; }

    public string Model => ObservedRequest.Model ?? "";

    public IReadOnlyList<HarnessWorkerMessage> Messages { get; }

    public IReadOnlyList<string> AvailableTools { get; }

    public string? LatestUserContent => Messages.LastOrDefault(message =>
        string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;

    public bool HasAnyToolResults => Messages.Any(message =>
        string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase));

    public static HarnessWorkerRequest Parse(HarnessObservedRequest observedRequest)
    {
        var messages = new List<HarnessWorkerMessage>();
        var availableTools = new List<string>();

        using var document = JsonDocument.Parse(observedRequest.RawBody);
        var root = document.RootElement;

        if (root.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageElement in messagesElement.EnumerateArray())
            {
                messages.Add(ParseMessage(messageElement));
            }
        }

        if (root.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolElement in toolsElement.EnumerateArray())
            {
                if (!toolElement.TryGetProperty("function", out var functionElement))
                {
                    continue;
                }

                if (!functionElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var toolName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    availableTools.Add(toolName);
                }
            }
        }

        return new HarnessWorkerRequest(observedRequest, messages, availableTools);
    }

    public bool HasAvailableTool(string toolName)
    {
        return AvailableTools.Contains(toolName, StringComparer.Ordinal);
    }

    public string BuildSummary()
    {
        var lastUser = LatestUserContent;
        var userSummary = string.IsNullOrWhiteSpace(lastUser)
            ? "no user content"
            : Truncate(lastUser);
        return $"model={Model}; messages={Messages.Count}; tools={string.Join(", ", AvailableTools)}; lastUser={userSummary}";
    }

    public bool TryGetLatestToolResult(string toolName, out string? content)
    {
        var toolNamesById = BuildToolNamesById();
        for (var index = Messages.Count - 1; index >= 0; index--)
        {
            var message = Messages[index];
            if (!string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (message.ToolCallId is null)
            {
                continue;
            }

            if (!toolNamesById.TryGetValue(message.ToolCallId, out var resolvedToolName))
            {
                continue;
            }

            if (string.Equals(resolvedToolName, toolName, StringComparison.Ordinal))
            {
                content = message.Content;
                return true;
            }
        }

        content = null;
        return false;
    }

    public string? ExtractSection(string heading)
    {
        return HarnessWorkerText.ExtractSection(LatestUserContent, heading);
    }

    public string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private Dictionary<string, string> BuildToolNamesById()
    {
        var toolNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in Messages)
        {
            foreach (var toolCall in message.ToolCalls)
            {
                toolNamesById[toolCall.Id] = toolCall.Name;
            }
        }

        return toolNamesById;
    }

    private static HarnessWorkerMessage ParseMessage(JsonElement messageElement)
    {
        var role = messageElement.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String
            ? roleElement.GetString() ?? "unknown"
            : "unknown";

        var content = messageElement.TryGetProperty("content", out var contentElement)
            ? ExtractMessageContent(contentElement)
            : null;

        var toolCallId = messageElement.TryGetProperty("tool_call_id", out var toolCallIdElement) && toolCallIdElement.ValueKind == JsonValueKind.String
            ? toolCallIdElement.GetString()
            : null;

        var toolCalls = new List<HarnessToolCallTrace>();
        if (messageElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCallElement in toolCallsElement.EnumerateArray())
            {
                if (!toolCallElement.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!toolCallElement.TryGetProperty("function", out var functionElement))
                {
                    continue;
                }

                if (!functionElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var argumentsJson = functionElement.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.String
                    ? argumentsElement.GetString() ?? "{}"
                    : "{}";

                toolCalls.Add(new HarnessToolCallTrace(
                    idElement.GetString() ?? "",
                    nameElement.GetString() ?? "",
                    argumentsJson));
            }
        }

        return new HarnessWorkerMessage
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
            ToolCalls = toolCalls,
        };
    }

    private static string? ExtractMessageContent(JsonElement contentElement)
    {
        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString(),
            JsonValueKind.Array => ExtractArrayContent(contentElement),
            JsonValueKind.Object => ExtractObjectContent(contentElement),
            _ => null,
        };
    }

    private static string? ExtractArrayContent(JsonElement arrayElement)
    {
        var parts = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            var part = ExtractObjectContent(item);
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part);
            }
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? ExtractObjectContent(JsonElement objectElement)
    {
        if (objectElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (objectElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            return textElement.GetString();
        }

        if (objectElement.TryGetProperty("content", out var nestedContentElement))
        {
            return ExtractMessageContent(nestedContentElement);
        }

        return null;
    }

    private static string Truncate(string value)
    {
        return value.Length <= 140 ? value : value[..140] + "...";
    }
}

public sealed record HarnessWorkerMessage
{
    public required string Role { get; init; }
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<HarnessToolCallTrace> ToolCalls { get; init; } = [];
}

internal static class HarnessWorkerText
{
    private static readonly Regex SectionRegexTemplate = new(
        @"^## (?<heading>.+?)\r?\n\r?\n(?<content>.*?)(?=^\#\# |\z)",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ProperNameRegex = new(
        @"\b[A-Z][a-z]{2,}\b",
        RegexOptions.Compiled);

    public static string? ExtractSection(string? text, string heading)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in SectionRegexTemplate.Matches(text))
        {
            var candidateHeading = match.Groups["heading"].Value.Trim();
            if (!string.Equals(candidateHeading, heading, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return match.Groups["content"].Value.Trim();
        }

        return null;
    }

    public static IReadOnlyList<string> ExtractCharacterNames(HarnessWorkerScenario scenario)
    {
        var corpus = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(scenario.Premise))
        {
            corpus.AppendLine(scenario.Premise);
        }

        foreach (var loreContent in scenario.LoreFiles.Values)
        {
            corpus.AppendLine(loreContent);
        }

        var ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "The",
            "And",
            "For",
            "With",
            "This",
            "That",
        };

        var names = ProperNameRegex.Matches(corpus.ToString())
            .Select(match => match.Value)
            .Where(name => !ignored.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return ["Aurora", "Lucian"];
        }

        if (names.Count == 1)
        {
            names.Add("Lucian");
        }

        return names;
    }

    public static IReadOnlyList<(string FileName, string Passage)> FindLorePassages(
        HarnessWorkerScenario scenario,
        string query)
    {
        var tokens = query.Split(
                [' ', '\r', '\n', '\t', ',', '.', '?', '!', ':', ';', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 4)
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matches = new List<(string FileName, string Passage)>();
        foreach (var pair in scenario.LoreFiles)
        {
            var sentences = pair.Value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var sentence in sentences)
            {
                if (tokens.Count == 0 || tokens.Any(token => sentence.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add((pair.Key, sentence.Trim()));
                }
            }
        }

        return matches
            .Distinct()
            .Take(4)
            .ToList();
    }
}

public sealed class PrototypePlannerHarnessWorker : IHarnessProviderWorker
{
    public HarnessWorkerRole Role => HarnessWorkerRole.Planner;

    public Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct)
    {
        var isDesignRequest = request.LatestUserContent?.Contains(
            "Review and refine this existing story design.",
            StringComparison.OrdinalIgnoreCase) == true;

        if (request.HasAnyToolResults)
        {
            return Task.FromResult(new HarnessWorkerResponse
            {
                Content = isDesignRequest ? "Design refinement complete." : "Planning complete.",
                ReasoningContent = isDesignRequest
                    ? "Refined the chapter brief against the outline and existing plan files."
                    : "Created the core planning artifacts and chapter brief.",
                FinishReason = "stop",
                Notes = isDesignRequest ? "planner-design-complete" : "planner-plan-complete",
            });
        }

        var names = HarnessWorkerText.ExtractCharacterNames(scenario);
        var firstLead = names[0];
        var secondLead = names[1];
        var premise = request.ExtractSection("Premise") ?? scenario.Premise;

        if (isDesignRequest)
        {
            return Task.FromResult(new HarnessWorkerResponse
            {
                ToolCalls =
                [
                    new HarnessToolCallPlan(
                        "design_brief",
                        "write_file",
                        request.SerializeJson(new
                        {
                            directory = "forge",
                            path = $"{scenario.ProjectName}/plan/ch-01-brief.md",
                            content =
                                $"# ch-01 brief\n" +
                                "Target word count: 700\n" +
                                $"Plot beats: {firstLead} must appear composed under scrutiny, {secondLead} quietly shields her, the sapphire ring remains hidden in the conservatory wall, and they leave the winter gala presenting a believable alliance.",
                        })),
                ],
                ReasoningContent = "Tightened the chapter brief so the first scene anchors on canon details and relationship pressure.",
                FinishReason = "tool_calls",
                Notes = "planner-design-rewrite",
            });
        }

        return Task.FromResult(new HarnessWorkerResponse
        {
            ToolCalls =
            [
                new HarnessToolCallPlan(
                    "plan_premise",
                    "write_file",
                    request.SerializeJson(new
                    {
                        directory = "forge",
                        path = $"{scenario.ProjectName}/plan/premise.md",
                        content = string.IsNullOrWhiteSpace(premise) ? scenario.Premise : premise,
                    })),
                new HarnessToolCallPlan(
                    "plan_outline",
                    "write_file",
                    request.SerializeJson(new
                    {
                        directory = "forge",
                        path = $"{scenario.ProjectName}/plan/outline.md",
                        content =
                            $"# Outline\n\n" +
                            "## ch-01\n" +
                            $"{firstLead} and {secondLead} survive the winter gala by presenting a united front while protecting the sapphire ring.\n\n" +
                            "## ch-02\n" +
                            $"The alliance hardens when outside pressure forces {firstLead} and {secondLead} into a more deliberate partnership.",
                    })),
                new HarnessToolCallPlan(
                    "plan_style",
                    "write_file",
                    request.SerializeJson(new
                    {
                        directory = "forge",
                        path = $"{scenario.ProjectName}/plan/style.md",
                        content = "# Style\nThird-person intimate romantic suspense with elegant, high-clarity atmosphere and controlled emotional escalation.",
                    })),
                new HarnessToolCallPlan(
                    "plan_bible",
                    "write_file",
                    request.SerializeJson(new
                    {
                        directory = "forge",
                        path = $"{scenario.ProjectName}/plan/bible.md",
                        content =
                            $"# Bible\n" +
                            $"- {firstLead} once hid the sapphire ring inside the conservatory wall.\n" +
                            $"- The arranged marriage contract binds {firstLead} and {secondLead} to present a united front.\n" +
                            "- The winter gala is the first public test of that alliance.",
                    })),
                new HarnessToolCallPlan(
                    "plan_brief",
                    "write_file",
                    request.SerializeJson(new
                    {
                        directory = "forge",
                        path = $"{scenario.ProjectName}/plan/ch-01-brief.md",
                        content =
                            "# ch-01 brief\n" +
                            "Target word count: 700\n" +
                            $"Plot beats: {firstLead} must appear composed, {secondLead} must help her keep the sapphire ring hidden, and they must leave the gala aligned.\n" +
                            "Continuity notes: keep the conservatory wall location explicit and the contract pressure visible.",
                    })),
            ],
            ReasoningContent = "Mapped the premise and lore into outline, style, bible, and a concrete first chapter brief.",
            FinishReason = "tool_calls",
            Notes = "planner-plan-artifacts",
        });
    }
}

public sealed class PrototypeWriterHarnessWorker : IHarnessProviderWorker
{
    public HarnessWorkerRole Role => HarnessWorkerRole.Writer;

    public Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct)
    {
        var names = HarnessWorkerText.ExtractCharacterNames(scenario);
        var firstLead = names[0];
        var secondLead = names[1];

        if (!request.TryGetLatestToolResult("query_lore", out var loreResultContent))
        {
            return Task.FromResult(new HarnessWorkerResponse
            {
                ToolCalls =
                [
                    new HarnessToolCallPlan(
                        "writer_lore",
                        "query_lore",
                        request.SerializeJson(new
                        {
                            query = $"Where is the sapphire ring hidden and what public pressure binds {firstLead} and {secondLead}?",
                        })),
                ],
                ReasoningContent = "Before drafting, verify the ring location and alliance pressure through lore.",
                FinishReason = "tool_calls",
                Notes = "writer-query-lore",
            });
        }

        var chapterBrief = request.ExtractSection("Chapter Brief") ?? "";
        var loreHighlights = ParseLoreHighlights(loreResultContent);
        var prose =
            $"{firstLead} crossed the conservatory with her shoulders perfectly level, as if the winter gala had asked nothing of her but grace. " +
            $"Behind the ivy lattice, the sapphire ring waited in its hidden seam along the conservatory wall, cold insurance against the contract that now bound her to {secondLead} before every watching guest.\n\n" +
            $"{secondLead} did not crowd her. He merely shifted into the line of sight of the nearest rivals and offered a quiet nod, an offer of cover disguised as effortless composure. " +
            $"Together they answered every test with a believable alliance until the music thinned and the hall finally released them, still aligned and still in possession of the secret no one else had earned.\n\n" +
            $"{(string.IsNullOrWhiteSpace(chapterBrief) ? "" : "The chapter keeps the brief’s focus on composure, concealment, and the public strain of the alliance. ")}" +
            $"{(string.IsNullOrWhiteSpace(loreHighlights) ? "" : loreHighlights)}".Trim();

        return Task.FromResult(new HarnessWorkerResponse
        {
            Content = prose.Trim(),
            ReasoningContent = "Drafted the scene using the verified ring location and contract pressure as the controlling constraints.",
            FinishReason = "stop",
            Notes = "writer-drafted-chapter",
        });
    }

    private static string ParseLoreHighlights(string? loreResultContent)
    {
        if (string.IsNullOrWhiteSpace(loreResultContent))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(loreResultContent);
            if (!document.RootElement.TryGetProperty("relevant_passages", out var passagesElement)
                || passagesElement.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            var passages = passagesElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (passages.Count == 0)
            {
                return "";
            }

            return $"Lore confirmed: {string.Join(" ", passages)}";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}

public sealed class PrototypeReviewerHarnessWorker : IHarnessProviderWorker
{
    public HarnessWorkerRole Role => HarnessWorkerRole.Reviewer;

    public Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct)
    {
        var draft = request.ExtractSection("Chapter Draft") ?? "";
        var extractedDetails = new List<string>();
        if (draft.Contains("sapphire ring", StringComparison.OrdinalIgnoreCase))
        {
            extractedDetails.Add("Aurora keeps the sapphire ring hidden inside the conservatory wall.");
        }

        if (draft.Contains("quiet", StringComparison.OrdinalIgnoreCase) || draft.Contains("nod", StringComparison.OrdinalIgnoreCase))
        {
            extractedDetails.Add("Lucian shields Aurora from rival scrutiny without making a scene.");
        }

        var response = request.SerializeJson(new
        {
            continuity = 9,
            brief_adherence = 9,
            voice_consistency = 8,
            quality = 8,
            feedback = "Strong opening chapter. Keep the alliance pressure visible and continue rewarding specific lore details.",
            extracted_details = extractedDetails,
        });

        return Task.FromResult(new HarnessWorkerResponse
        {
            Content = response,
            ReasoningContent = "Scored the chapter against continuity, brief adherence, voice, and quality, then extracted future-facing details.",
            FinishReason = "stop",
            Notes = "reviewer-pass-with-details",
        });
    }
}

public sealed class PrototypeLibrarianHarnessWorker : IHarnessProviderWorker
{
    public HarnessWorkerRole Role => HarnessWorkerRole.Librarian;

    public Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct)
    {
        var query = request.LatestUserContent ?? "";
        var passages = HarnessWorkerText.FindLorePassages(scenario, query);

        var response = request.SerializeJson(new
        {
            relevant_passages = passages.Select(passage => passage.Passage).ToList(),
            source_files = passages.Select(passage => passage.FileName).ToList(),
            confidence = passages.Count > 0 ? "high" : "low",
        });

        return Task.FromResult(new HarnessWorkerResponse
        {
            Content = response,
            ReasoningContent = "Returned only the passages that matched the query terms from the configured lore set.",
            FinishReason = "stop",
            Notes = "librarian-json-response",
        });
    }
}

public sealed class PrototypeGeneralChatHarnessWorker : IHarnessProviderWorker
{
    public HarnessWorkerRole Role => HarnessWorkerRole.GeneralChat;

    public Task<HarnessWorkerResponse> GenerateAsync(
        HarnessWorkerRequest request,
        HarnessWorkerScenario scenario,
        CancellationToken ct)
    {
        var lastUser = request.LatestUserContent ?? "No user message provided.";
        return Task.FromResult(new HarnessWorkerResponse
        {
            Content = $"Worker-backed harness response: {lastUser}",
            ReasoningContent = "Summarized the latest user message for a lightweight general-chat exploratory reply.",
            FinishReason = "stop",
            Notes = "general-chat-summary",
        });
    }
}
