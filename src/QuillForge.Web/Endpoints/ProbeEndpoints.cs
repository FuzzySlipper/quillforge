using System.Text;
using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class ProbeEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapProbeEndpoints(this WebApplication app, string contentRoot)
    {
        app.MapPost("/api/probe", async (
            HttpContext httpContext,
            ICompletionService completionService,
            OrchestratorAgent orchestrator,
            IEnumerable<IToolHandler> toolHandlers,
            ISessionProfileReadService profileReadService,
            IConductorStore conductorStore,
            AppConfig appConfig,
            AtomicFileWriter fileWriter,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Resolve session context
            var sessionId = httpContext.TryGetSessionId();
            var prepared = await profileReadService.PrepareInteractiveRequestAsync(
                sessionId,
                new PrepareInteractiveRequestOptions(),
                ct);
            var sessionState = prepared.ProfileView.SessionState;
            var sessionContext = prepared.SessionContext;
            var activeModeName = sessionState.Mode.ActiveModeName;
            var model = appConfig.Models.Orchestrator;

            // Resolve the active mode and build its system prompt section
            var activeMode = orchestrator.ResolveMode(activeModeName);
            var modeContext = new ModeContext
            {
                ProjectName = sessionContext.ProjectName,
                CurrentFile = sessionContext.CurrentFile,
                CharacterSection = sessionContext.CharacterSection,
                StoryStateSummary = sessionContext.StoryStateSummary,
                FileContext = sessionContext.FileContext,
                WriterPendingContent = sessionContext.WriterPendingContent,
                ActiveLoreSet = prepared.ProfileView.ActiveLoreSet,
            };
            var modeSection = activeMode.BuildSystemPromptSection(modeContext);

            // Load conductor prompt
            var conductorPromptText = "";
            try
            {
                conductorPromptText = await conductorStore.LoadAsync(
                    prepared.Conductor,
                    appConfig.Persona.MaxTokens,
                    ct);
            }
            catch { /* conductor may not exist */ }

            // Reconstruct the system prompt (same logic as OrchestratorAgent.BuildSystemPrompt)
            var loreSection = string.IsNullOrWhiteSpace(modeContext.ActiveLoreSet)
                ? ""
                : $"\n\n## Active Lore Set\n\nThe current lore set is \"{modeContext.ActiveLoreSet}\". "
                  + "When using `query_lore`, results come from this lore set. "
                  + "Ground your lore references and world-building in this set's content.";
            var systemPrompt = $"{conductorPromptText}\n\n{modeSection}{loreSection}";

            // Collect tool definitions
            var toolDefs = toolHandlers.Select(t => t.Definition).ToList();

            // Build the probe prompt
            var probePrompt = ProbeBattery.BuildProbePrompt(systemPrompt, toolDefs, activeModeName);

            logger.LogInformation(
                "Running interpretation probe: mode={Mode}, model={Model}, tools={ToolCount}, scenarios={ScenarioCount}",
                activeModeName, model, toolDefs.Count, ProbeBattery.Scenarios.Count);

            // Stream SSE response
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var responseText = new StringBuilder();
            int inputTokens = 0, outputTokens = 0;

            var request = new CompletionRequest
            {
                Model = model,
                MaxTokens = 14096,
                SystemPrompt = null,
                Messages = [new CompletionMessage("user", new MessageContent(probePrompt))],
                Tools = null,
            };

            await foreach (var evt in completionService.StreamAsync(request, ct))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        responseText.Append(text.Text);
                        var textEvent = JsonSerializer.Serialize(new { Type = "text_delta", Text = text.Text }, s_jsonOptions);
                        await httpContext.Response.WriteAsync($"data: {textEvent}\n\n", ct);
                        await httpContext.Response.Body.FlushAsync(ct);
                        break;
                    case DoneEvent done:
                        inputTokens = done.Usage.InputTokens;
                        outputTokens = done.Usage.OutputTokens;
                        break;
                }
            }

            var content = responseText.ToString();

            // Persist the probe report as markdown
            var timestamp = DateTimeOffset.UtcNow;
            var sanitizedModel = model.Replace('/', '-').Replace(':', '-');
            var fileName = $"probe-{timestamp:yyyyMMdd-HHmmss}-{sanitizedModel}.md";
            var reportPath = Path.Combine(ContentPaths.DataLlmDebug, fileName);

            var report = new StringBuilder();
            report.AppendLine("---");
            report.AppendLine($"timestamp: {timestamp:O}");
            report.AppendLine($"model: {model}");
            report.AppendLine($"mode: {activeModeName}");
            report.AppendLine($"battery_version: \"{ProbeBattery.Version}\"");
            report.AppendLine($"scenario_count: {ProbeBattery.Scenarios.Count}");
            report.AppendLine($"tool_count: {toolDefs.Count}");
            report.AppendLine($"input_tokens: {inputTokens}");
            report.AppendLine($"output_tokens: {outputTokens}");
            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine($"# Interpretation Probe — {model}");
            report.AppendLine();
            report.AppendLine($"Mode: **{activeModeName}** | Battery: v{ProbeBattery.Version} | {timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine();
            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine(content);

            try
            {
                var fullDir = Path.Combine(contentRoot, ContentPaths.DataLlmDebug);
                Directory.CreateDirectory(fullDir);
                var fullPath = Path.Combine(fullDir, fileName);
                await fileWriter.WriteAsync(fullPath, report.ToString(), ct);
                logger.LogInformation("Probe report saved to {Path}", fullPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist probe report");
            }

            // Send done event
            var doneEvent = JsonSerializer.Serialize(new
            {
                Type = "done",
                Content = content,
                StopReason = "end_turn",
            }, s_jsonOptions);
            await httpContext.Response.WriteAsync($"data: {doneEvent}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        });
    }

}
