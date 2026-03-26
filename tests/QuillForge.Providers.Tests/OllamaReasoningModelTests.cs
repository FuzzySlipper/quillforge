using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Registry;

namespace QuillForge.Providers.Tests;

/// <summary>
/// Integration tests specifically for reasoning models (qwen3.5:9b).
/// Reasoning models emit thinking/reasoning blocks before their response,
/// which can cause issues with:
/// - Empty text content blocks
/// - Reasoning blocks mixed with text deltas in streaming
/// - Blank content after reasoning is stripped
/// - Stop reasons differing from non-reasoning models
/// </summary>
[Trait("Category", "Integration")]
public class OllamaReasoningModelTests
{
    private const string OllamaBaseUrl = "http://192.168.1.10:11434";
    private const string ReasoningModel = "qwen3.5:9b";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static ProviderRegistry CreateRegistry()
    {
        var factory = new ProviderFactory(LogFactory.CreateLogger<ProviderFactory>());
        var registry = new ProviderRegistry(factory,
            LogFactory.CreateLogger<ProviderRegistry>(), LogFactory);

        registry.Register(new ProviderConfig
        {
            Alias = "ollama-reasoning",
            Type = ProviderType.Ollama,
            ApiKey = "ollama",
            BaseUrl = OllamaBaseUrl,
            DefaultModel = ReasoningModel,
        });

        return registry;
    }

    private static async Task<bool> IsOllamaAvailable()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{OllamaBaseUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task ReasoningModel_Completion_ReturnsNonEmptyText()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama-reasoning");

        var request = new CompletionRequest
        {
            Model = ReasoningModel,
            MaxTokens = 200,
            Messages = [new CompletionMessage("user", new MessageContent("What is 2+2? Answer with just the number."))],
        };

        var response = await service.CompleteAsync(request, cts.Token);
        var text = response.Content.GetText();

        // Reasoning model should still produce visible text output, not just reasoning
        Assert.False(string.IsNullOrWhiteSpace(text),
            "Reasoning model returned blank text — reasoning blocks may not be converted to text properly");
        Assert.Contains("4", text);
    }

    [Fact]
    public async Task ReasoningModel_Streaming_HandlesReasoningAndTextBlocks()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama-reasoning");

        var request = new CompletionRequest
        {
            Model = ReasoningModel,
            MaxTokens = 200,
            Messages = [new CompletionMessage("user", new MessageContent("What is the capital of France? One word answer."))],
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(request, cts.Token))
        {
            events.Add(evt);
        }

        // Should have a DoneEvent regardless
        Assert.Contains(events, e => e is DoneEvent);

        // Should have at least some text output
        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        var reasoningEvents = events.OfType<ReasoningDeltaEvent>().ToList();

        // Reasoning model might emit ReasoningDeltaEvents, TextDeltaEvents, or both
        var totalContentEvents = textEvents.Count + reasoningEvents.Count;
        Assert.True(totalContentEvents > 0,
            $"No content events received. Events: {string.Join(", ", events.Select(e => e.GetType().Name))}");

        // The actual answer text should be present
        var fullText = string.Join("", textEvents.Select(e => e.Text));
        Assert.False(string.IsNullOrWhiteSpace(fullText),
            $"Text deltas were empty. Got {reasoningEvents.Count} reasoning events, {textEvents.Count} text events. " +
            $"The adapter may not be converting reasoning model output correctly.");
    }

    [Fact]
    public async Task ReasoningModel_Streaming_DoneEventHasValidStopReason()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama-reasoning");

        var request = new CompletionRequest
        {
            Model = ReasoningModel,
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Say hi."))],
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(request, cts.Token))
        {
            events.Add(evt);
        }

        var done = events.OfType<DoneEvent>().LastOrDefault();
        Assert.NotNull(done);

        // Stop reason should be a recognized value, not null or empty
        Assert.False(string.IsNullOrWhiteSpace(done.StopReason),
            "DoneEvent.StopReason was blank from reasoning model");
        var validReasons = new[] { "end_turn", "max_tokens", "stop" };
        Assert.Contains(done.StopReason, validReasons);
    }

    [Fact]
    public async Task ReasoningModel_StructuredOutput_CanProduceJson()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama-reasoning");

        var request = new CompletionRequest
        {
            Model = ReasoningModel,
            MaxTokens = 300,
            SystemPrompt = "Respond ONLY with a JSON object. No other text.",
            Messages = [new CompletionMessage("user", new MessageContent(
                """Rate this sentence on a 1-10 scale: "The sun rose over the mountains." Respond with: {"score": <number>, "reason": "<brief reason>"}"""))],
        };

        var response = await service.CompleteAsync(request, cts.Token);
        var text = response.Content.GetText().Trim();

        // The response should contain valid-looking JSON somewhere
        // (reasoning models sometimes wrap JSON in explanation)
        Assert.False(string.IsNullOrWhiteSpace(text),
            "Reasoning model returned empty text for structured output request");
        Assert.True(text.Contains('{') && text.Contains('}'),
            $"Expected JSON in response but got: {Truncate(text, 200)}");
    }

    [Fact]
    public async Task ReasoningModel_MultiTurn_MaintainsContext()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama-reasoning");

        var request = new CompletionRequest
        {
            Model = ReasoningModel,
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Remember this word: AURORA")),
                new CompletionMessage("assistant", new MessageContent("I'll remember the word AURORA.")),
                new CompletionMessage("user", new MessageContent("What word did I ask you to remember?")),
            ],
        };

        var response = await service.CompleteAsync(request, cts.Token);
        var text = response.Content.GetText().ToUpperInvariant();

        Assert.Contains("AURORA", text);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
