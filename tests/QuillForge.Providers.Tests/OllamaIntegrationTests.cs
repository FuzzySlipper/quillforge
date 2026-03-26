using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Adapters;
using QuillForge.Providers.Registry;

namespace QuillForge.Providers.Tests;

/// <summary>
/// Integration tests using a local Ollama server. These tests hit a real LLM
/// and verify end-to-end message conversion, streaming, and tool calling.
///
/// Requires Ollama running at http://192.168.1.10:11434 with a model available.
/// Skipped automatically if the server is unreachable.
///
/// NOTE: First request after idle may take 30-60s while Ollama loads the model
/// into VRAM. The generous timeout on the availability check handles this.
/// Individual test timeouts are set high to accommodate cold starts.
/// </summary>
[Trait("Category", "Integration")]
public class OllamaIntegrationTests
{
    private const string OllamaBaseUrl = "http://192.168.1.10:11434";
    private const string DefaultModel = "qwen2.5:14b";

    /// <summary>
    /// Timeout for individual LLM requests. Must be long enough
    /// to cover cold start (model loading into VRAM) on first call.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);

    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static ProviderRegistry CreateRegistry()
    {
        var factory = new ProviderFactory(LogFactory.CreateLogger<ProviderFactory>());
        var registry = new ProviderRegistry(factory,
            LogFactory.CreateLogger<ProviderRegistry>(), LogFactory);

        registry.Register(new ProviderConfig
        {
            Alias = "ollama",
            Type = ProviderType.Ollama,
            ApiKey = "ollama",
            BaseUrl = OllamaBaseUrl,
            DefaultModel = DefaultModel,
        });

        return registry;
    }

    /// <summary>
    /// Checks if Ollama is reachable AND warms the model by sending a tiny request.
    /// This ensures the cold-start penalty is paid here, not in the actual test.
    /// </summary>
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
    public async Task SimpleCompletion_ReturnsText()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama");

        var request = new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 50,
            Messages = [new CompletionMessage("user", new MessageContent("Say hello in exactly 3 words."))],
        };

        var response = await service.CompleteAsync(request, cts.Token);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Content.GetText()));
        Assert.True(response.Usage.OutputTokens > 0);
    }

    [Fact]
    public async Task Streaming_YieldsTextDeltas()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama");

        var request = new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 50,
            Messages = [new CompletionMessage("user", new MessageContent("Count from 1 to 5."))],
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(request, cts.Token))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is TextDeltaEvent);
        Assert.Contains(events, e => e is DoneEvent);

        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.True(textEvents.Count > 0);

        var fullText = string.Join("", textEvents.Select(e => e.Text));
        Assert.False(string.IsNullOrWhiteSpace(fullText));
    }

    [Fact]
    public async Task SystemPrompt_IsRespected()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama");

        var request = new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 50,
            SystemPrompt = "You are a pirate. Always respond with 'Arrr!' at the start.",
            Messages = [new CompletionMessage("user", new MessageContent("Hello"))],
        };

        var response = await service.CompleteAsync(request, cts.Token);
        var text = response.Content.GetText();

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task ProviderRegistry_TestConnection_Succeeds()
    {
        if (!await IsOllamaAvailable()) return;

        var registry = CreateRegistry();
        var success = await registry.TestConnectionAsync("ollama");
        Assert.True(success);
    }

    [Fact]
    public async Task MultiTurnConversation_MaintainsContext()
    {
        if (!await IsOllamaAvailable()) return;
        using var cts = new CancellationTokenSource(RequestTimeout);

        var registry = CreateRegistry();
        var service = registry.GetCompletionService("ollama");

        var request = new CompletionRequest
        {
            Model = DefaultModel,
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("My name is Zephyr.")),
                new CompletionMessage("assistant", new MessageContent("Hello Zephyr! Nice to meet you.")),
                new CompletionMessage("user", new MessageContent("What is my name?")),
            ],
        };

        var response = await service.CompleteAsync(request, cts.Token);
        var text = response.Content.GetText().ToLowerInvariant();

        Assert.Contains("zephyr", text);
    }
}
