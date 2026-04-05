using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Sends a bug report or feature request to the developer via email.
/// The actual SMTP/email sending is delegated to an IEmailService interface.
/// </summary>
public sealed class EmailDeveloperHandler : IToolHandler
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailDeveloperHandler> _logger;

    public EmailDeveloperHandler(IEmailService emailService, ILogger<EmailDeveloperHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public string Name => "email_developer";

    public ToolDefinition Definition => new(Name,
        "Send a bug report or feature request to the developer.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "subject": { "type": "string", "description": "Email subject line" },
                    "body": { "type": "string", "description": "Email body with details" },
                    "type": { "type": "string", "enum": ["bug", "feature"], "description": "Report type" }
                },
                "required": ["subject", "body"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var subject = input.GetRequiredString("subject");
        var body = input.GetRequiredString("body");
        var type = input.GetOptionalString("type") ?? "bug";

        _logger.LogInformation("EmailDeveloperHandler: sending {Type} report: \"{Subject}\"", type, subject);

        await _emailService.SendDeveloperEmailAsync($"[{type.ToUpperInvariant()}] {subject}", body, ct);
        return ToolResult.Ok($"Email sent to developer: {subject}");
    }
}
