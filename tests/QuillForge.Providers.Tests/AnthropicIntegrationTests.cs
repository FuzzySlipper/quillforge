using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Registry;

namespace QuillForge.Providers.Tests;

/// <summary>
/// Smoke tests against the live Anthropic API.
/// Skipped if ANTHROPIC_API_KEY is not set. Use small token budgets.
///
/// Run with: dotnet test --filter Category=LiveProvider
/// </summary>
[Trait("Category", "LiveProvider")]
public class AnthropicIntegrationTests
{
    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static string? GetApiKey() => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    private static ProviderRegistry CreateRegistry(string apiKey)
    {
        var factory = new ProviderFactory(LogFactory.CreateLogger<ProviderFactory>());
        var registry = new ProviderRegistry(factory, new AppConfig(),
            LogFactory.CreateLogger<ProviderRegistry>(), LogFactory);

        registry.Register(new ProviderConfig
        {
            Alias = "anthropic",
            Type = ProviderType.Anthropic,
            ApiKey = apiKey,
            DefaultModel = DefaultModel,
        });

        return registry;
    }

    [Fact]
    public async Task SimpleCompletion_ReturnsText()
    {
        var apiKey = GetApiKey();
        if (apiKey is null) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry(apiKey);
        var service = registry.GetCompletionService("anthropic");

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Say hello in exactly 3 words."))],
        }, cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.Content.GetText()));
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.True(response.Usage.OutputTokens > 0);
    }

    [Fact]
    public async Task Streaming_YieldsTextDeltasAndDone()
    {
        var apiKey = GetApiKey();
        if (apiKey is null) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry(apiKey);
        var service = registry.GetCompletionService("anthropic");

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Count from 1 to 5."))],
        }, cts.Token))
        {
            events.Add(evt);
        }

        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.True(textEvents.Count > 0, "Expected at least one TextDeltaEvent");

        var doneEvents = events.OfType<DoneEvent>().ToList();
        Assert.Single(doneEvents);
        Assert.Equal(StopReason.EndTurn, doneEvents[0].StopReason);
    }

    [Fact]
    public async Task ToolCall_ModelCallsDefinedTool()
    {
        var apiKey = GetApiKey();
        if (apiKey is null) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry(apiKey);
        var service = registry.GetCompletionService("anthropic");

        var toolSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "city": { "type": "string", "description": "The city name" }
                },
                "required": ["city"]
            }
            """).RootElement;

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 200,
            Messages = [new CompletionMessage("user", new MessageContent("What's the weather in Paris? Use the get_weather tool."))],
            Tools = [new ToolDefinition("get_weather", "Get current weather for a city", toolSchema)],
        }, cts.Token);

        Assert.Equal(StopReason.ToolUse, response.StopReason);
        var toolCalls = response.Content.GetToolCalls().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("get_weather", toolCalls[0].Name);
    }

    [Fact]
    public async Task SystemPrompt_IsRespected()
    {
        var apiKey = GetApiKey();
        if (apiKey is null) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry(apiKey);
        var service = registry.GetCompletionService("anthropic");

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 100,
            SystemPrompt = "You must respond with exactly the word 'PINEAPPLE' and nothing else.",
            Messages = [new CompletionMessage("user", new MessageContent("Hello"))],
        }, cts.Token);

        var text = response.Content.GetText();
        Assert.Contains("PINEAPPLE", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopReason_EndTurnForNormalCompletion()
    {
        var apiKey = GetApiKey();
        if (apiKey is null) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry(apiKey);
        var service = registry.GetCompletionService("anthropic");

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 200,
            Messages = [new CompletionMessage("user", new MessageContent("Say 'done'."))],
        }, cts.Token);

        Assert.Equal(StopReason.EndTurn, response.StopReason);
    }
}
