using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests.Scenarios;

/// <summary>
/// Executes a <see cref="ScenarioDefinition"/> step-by-step against in-process services.
/// Collects a pass/fail report for each step's expectations.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly OrchestratorAgent _orchestrator;
    private readonly ISessionStateService _runtimeService;
    private readonly ISessionBootstrapService _bootstrapService;
    private readonly ISessionStore _sessionStore;
    private readonly ISessionProfileReadService _profileReadService;
    private readonly IReadOnlyList<IToolHandler> _tools;
    private readonly ILogger _logger;

    public ScenarioRunner(
        OrchestratorAgent orchestrator,
        ISessionStateService runtimeService,
        ISessionBootstrapService bootstrapService,
        ISessionStore sessionStore,
        ISessionProfileReadService profileReadService,
        IEnumerable<IToolHandler> tools,
        ILogger logger)
    {
        _orchestrator = orchestrator;
        _runtimeService = runtimeService;
        _bootstrapService = bootstrapService;
        _sessionStore = sessionStore;
        _profileReadService = profileReadService;
        _tools = tools.ToList();
        _logger = logger;
    }

    public async Task<ScenarioReport> RunAsync(
        ScenarioDefinition scenario,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Scenario: {Name} — {StepCount} steps", scenario.Name, scenario.Steps.Count);

        var report = new ScenarioReport { ScenarioName = scenario.Name };

        // Create session
        var tree = await _bootstrapService.CreateAsync(
            new CreateSessionCommand { Name = $"Scenario: {scenario.Name}" }, ct);
        var sessionId = tree.SessionId;

        // Set profile if specified
        if (!string.IsNullOrEmpty(scenario.Profile))
        {
                await _runtimeService.SetProfileAsync(
                    sessionId,
                    new SetSessionProfileCommand(scenario.Profile, null, null, null, null),
                    ct);
        }

        for (int i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];
            var stepResult = new StepResult { StepIndex = i };

            try
            {
                if (step.Chat is { } chatMessage)
                {
                    stepResult.Action = $"chat: {chatMessage}";
                    var chatResult = await ExecuteChatStepAsync(sessionId, chatMessage, tree, ct);
                    stepResult.FinalContent = chatResult.FinalContent;
                    stepResult.ToolsCalled = chatResult.ToolsCalled;
                    stepResult.StopReason = chatResult.StopReason;
                    tree = chatResult.Tree;
                }
                else if (step.SetMode is { } modeName)
                {
                    stepResult.Action = $"set_mode: {modeName}";
                    var result = await _runtimeService.SetModeAsync(
                        sessionId,
                        new SetSessionModeCommand(modeName, null, null, null),
                        ct);
                    if (result.Status != SessionMutationStatus.Success)
                        stepResult.Failures.Add($"Mode switch failed: {result.Error}");
                }
                else if (step.SetProfile is { } profileId)
                {
                    stepResult.Action = $"set_profile: {profileId}";
                    await _runtimeService.SetProfileAsync(
                        sessionId,
                        new SetSessionProfileCommand(profileId, null, null, null, null),
                        ct);
                }
                else if (step.ReloadSession)
                {
                    stepResult.Action = "reload_session";
                    await _sessionStore.SaveAsync(tree, ct);
                    tree = await _sessionStore.LoadAsync(sessionId, ct);
                }

                // Evaluate expectations
                if (step.Expect is { } expect)
                {
                    await EvaluateExpectationsAsync(expect, stepResult, sessionId, tree, ct);
                }
            }
            catch (Exception ex)
            {
                stepResult.Failures.Add($"Exception: {ex.GetType().Name}: {ex.Message}");
            }

            report.Steps.Add(stepResult);
            _logger.LogInformation(
                "  Step {Index}: {Action} — {Status}",
                i, stepResult.Action, stepResult.Passed ? "PASS" : "FAIL");
        }

        return report;
    }

    private async Task<ChatStepResult> ExecuteChatStepAsync(
        Guid sessionId,
        string message,
        ConversationTree tree,
        CancellationToken ct)
    {
        tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));

        var thread = tree.ToFlatThread();
        var messages = thread.Select(n => n.ToCompletionMessage()).ToList();
        var lastAssistant = thread
            .LastOrDefault(n => string.Equals(n.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            ?.Content.GetText();

        var prepared = await _profileReadService.PrepareInteractiveRequestAsync(
            sessionId,
            new PrepareInteractiveRequestOptions
            {
                LastAssistantResponse = lastAssistant,
            },
            ct);
        var sessionState = prepared.ProfileView.SessionState;
        var context = prepared.AgentContext;

        var assistantText = new System.Text.StringBuilder();
        var assistantReasoning = new System.Text.StringBuilder();
        StopReason? stopReason = null;
        var toolsCalled = new List<string>();
        ProviderReplayEnvelope? providerReplay = null;

        await foreach (var evt in _orchestrator.HandleStreamAsync(
            sessionState, "default", 4096, _tools, messages, context, ct: ct))
        {
            switch (evt)
            {
                case TextDeltaEvent text:
                    assistantText.Append(text.Text);
                    break;
                case ReasoningDeltaEvent reasoning:
                    assistantReasoning.Append(reasoning.Text);
                    break;
                case ToolCallValidatedEvent tool:
                    assistantText.Clear();
                    assistantReasoning.Clear();
                    toolsCalled.Add(tool.ToolName);
                    break;
                case DoneEvent done:
                    stopReason = done.StopReason;
                    providerReplay = done.ProviderReplay;
                    break;
            }
        }

        var finalReasoning = GetReasoningForDisplay(assistantReasoning, providerReplay);
        if (assistantText.Length > 0)
        {
            tree.Append(tree.ActiveLeafId, "assistant",
                new MessageContent(assistantText.ToString()),
                new MessageMetadata
                {
                    StopReason = stopReason,
                    Reasoning = finalReasoning,
                    ProviderReplay = providerReplay,
                });
        }

        await _sessionStore.SaveAsync(tree, ct);

        // Writer pending capture
        var runtimeService = _runtimeService;
        await runtimeService.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(assistantText.ToString(), sessionState.Mode.ActiveMode),
            ct);

        return new ChatStepResult
        {
            FinalContent = assistantText.ToString(),
            ToolsCalled = toolsCalled,
            StopReason = (stopReason ?? StopReason.EndTurn).ToWireString(),
            Tree = tree,
        };
    }

    private static string? GetReasoningForDisplay(
        System.Text.StringBuilder streamedReasoning,
        ProviderReplayEnvelope? providerReplay)
    {
        if (streamedReasoning.Length > 0)
        {
            return streamedReasoning.ToString();
        }

        return providerReplay is ReasoningReplayEnvelope reasoningReplay
            ? reasoningReplay.ReasoningContent
            : null;
    }

    private async Task EvaluateExpectationsAsync(
        StepExpectation expect,
        StepResult result,
        Guid sessionId,
        ConversationTree tree,
        CancellationToken ct)
    {
        var state = await _runtimeService.LoadViewAsync(sessionId, ct);

        if (expect.Mode is { } expectedMode)
        {
            var actualMode = state.Mode.ActiveMode.ToWireString();
            if (!string.Equals(actualMode, expectedMode, StringComparison.OrdinalIgnoreCase))
                result.Failures.Add($"mode: expected '{expectedMode}', got '{actualMode}'");
        }

        if (expect.WriterState is { } expectedWriterState)
        {
            var actualWriterState = state.Writer.State.ToString().ToLowerInvariant();
            if (!string.Equals(actualWriterState, expectedWriterState, StringComparison.OrdinalIgnoreCase))
                result.Failures.Add($"writer_state: expected '{expectedWriterState}', got '{actualWriterState}'");
        }

        if (expect.ToolCalled is { } expectedTool && result.ToolsCalled is { } tools)
        {
            if (!tools.Any(t => string.Equals(t, expectedTool, StringComparison.OrdinalIgnoreCase)))
                result.Failures.Add($"tool_called: expected '{expectedTool}', but tools called were: [{string.Join(", ", tools)}]");
        }

        if (expect.ContentContains is { } expectedContent && result.FinalContent is { } content)
        {
            if (!content.Contains(expectedContent, StringComparison.OrdinalIgnoreCase))
                result.Failures.Add($"content_contains: expected content to contain '{expectedContent}'");
        }

        if (expect.MessageCountGte is { } minCount)
        {
            var actualCount = tree.ToFlatThread().Count;
            if (actualCount < minCount)
                result.Failures.Add($"message_count_gte: expected >= {minCount}, got {actualCount}");
        }

        if (expect.StopReasonValue is { } expectedStop && result.StopReason is { } actualStop)
        {
            if (!string.Equals(actualStop, expectedStop, StringComparison.OrdinalIgnoreCase))
                result.Failures.Add($"stop_reason: expected '{expectedStop}', got '{actualStop}'");
        }

        if (expect.HasContent == true && string.IsNullOrWhiteSpace(result.FinalContent))
        {
            result.Failures.Add("has_content: expected non-empty content, but got empty");
        }
    }

    private sealed class ChatStepResult
    {
        public string FinalContent { get; init; } = "";
        public List<string> ToolsCalled { get; init; } = [];
        public string StopReason { get; init; } = "end_turn";
        public ConversationTree Tree { get; init; } = null!;
    }
}

public sealed class ScenarioReport
{
    public string ScenarioName { get; set; } = "";
    public List<StepResult> Steps { get; set; } = [];
    public bool Passed => Steps.All(s => s.Passed);
}

public sealed class StepResult
{
    public int StepIndex { get; set; }
    public string Action { get; set; } = "";
    public string? FinalContent { get; set; }
    public List<string>? ToolsCalled { get; set; }
    public string? StopReason { get; set; }
    public List<string> Failures { get; set; } = [];
    public bool Passed => Failures.Count == 0;
}
