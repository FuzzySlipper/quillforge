using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates a factual/technical question to a focused agent for a concise answer.
/// </summary>
public sealed class DelegateTechnicalHandler : IToolHandler
{
    private readonly ICompletionService _completionService;
    private readonly ILogger<DelegateTechnicalHandler> _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    public DelegateTechnicalHandler(ICompletionService completionService, AppConfig appConfig, ILogger<DelegateTechnicalHandler> logger)
    {
        _completionService = completionService;
        _logger = logger;
        _model = appConfig.Models.DelegateTechnical;
        _maxTokens = appConfig.Agents.DelegateTechnical.MaxTokens;
    }

    public string Name => "delegate_technical";

    public ToolDefinition Definition => new(Name,
        "Route a factual or technical question to a focused agent for a concise, accurate answer.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "question": { "type": "string", "description": "The factual or technical question" }
                },
                "required": ["question"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var question = input.GetProperty("question").GetString() ?? "";
        _logger.LogDebug("DelegateTechnicalHandler: delegating question");

        var request = new CompletionRequest
        {
            Model = _model,
            MaxTokens = _maxTokens,
            SystemPrompt = "You are a knowledgeable assistant. Answer the question concisely and accurately.",
            Messages = [new CompletionMessage("user", new MessageContent(question))],
        };

        var response = await _completionService.CompleteAsync(request, ct);
        return ToolResult.Ok(response.Content.GetText());
    }
}
