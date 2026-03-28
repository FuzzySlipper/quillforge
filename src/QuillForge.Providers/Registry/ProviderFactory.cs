using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace QuillForge.Providers.Registry;

/// <summary>
/// Creates IChatClient instances from provider configuration.
/// Each provider type has its own factory logic.
/// </summary>
public sealed class ProviderFactory
{
    private readonly ILogger<ProviderFactory> _logger;

    public ProviderFactory(ILogger<ProviderFactory> logger)
    {
        _logger = logger;
    }

    public IChatClient CreateClient(ProviderConfig config)
    {
        _logger.LogInformation(
            "Creating chat client for provider {Alias} (type={Type})",
            config.Alias, config.Type);

        return config.Type switch
        {
            ProviderType.OpenAI => CreateOpenAIClient(config),
            ProviderType.Anthropic => CreateAnthropicClient(config),
            ProviderType.Ollama => CreateOllamaClient(config),
            ProviderType.OpenRouter => CreateOpenRouterClient(config),
            ProviderType.AzureOpenAI => CreateAzureOpenAIClient(config),
            ProviderType.Custom => CreateCustomOpenAIClient(config),
            _ => throw new ArgumentException($"Unsupported provider type: {config.Type}"),
        };
    }

    private IChatClient CreateOpenAIClient(ProviderConfig config)
    {
        OpenAIClient client;
        if (config.BaseUrl is not null)
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(config.BaseUrl) };
            client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
        }
        else
        {
            client = new OpenAIClient(new ApiKeyCredential(config.ApiKey));
        }

        var model = config.DefaultModel ?? "gpt-4o";

        _logger.LogDebug("Created OpenAI client at {BaseUrl}, model={Model}", config.BaseUrl ?? "https://api.openai.com", model);
        return client.GetChatClient(model).AsIChatClient();
    }

    /// <summary>
    /// Detects reasoning-enabled models that require reasoning_content preservation.
    /// </summary>
    public static bool IsReasoningModel(string model)
    {
        var m = model.ToLowerInvariant();
        return m.Contains("kimi-k2") || m.Contains("deepseek-r") || m.Contains("qwq")
            || m.Contains("o1") || m.Contains("o3") || m.Contains("o4");
    }

    private IChatClient CreateAnthropicClient(ProviderConfig config)
    {
        var options = new Anthropic.Core.ClientOptions { ApiKey = config.ApiKey };
        if (config.BaseUrl is not null) options.BaseUrl = config.BaseUrl;
        var client = new AnthropicClient(options);
        var model = config.DefaultModel ?? "claude-sonnet-4-20250514";

        _logger.LogDebug("Created Anthropic client, default model={Model}", model);
        return client.AsIChatClient(model);
    }

    private IChatClient CreateOllamaClient(ProviderConfig config)
    {
        var baseUrl = config.BaseUrl ?? "http://localhost:11434";
        var options = new OpenAIClientOptions { Endpoint = new Uri($"{baseUrl}/v1") };
        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey ?? "ollama"), options);
        var model = config.DefaultModel ?? "llama3";

        _logger.LogDebug("Created Ollama client at {BaseUrl}, model={Model}", baseUrl, model);
        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient CreateOpenRouterClient(ProviderConfig config)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") };
        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
        var model = config.DefaultModel ?? "anthropic/claude-sonnet-4-20250514";

        _logger.LogDebug("Created OpenRouter client, model={Model}", model);
        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient CreateAzureOpenAIClient(ProviderConfig config)
    {
        var endpoint = config.BaseUrl ?? throw new ArgumentException("Azure OpenAI requires a base URL (endpoint).");
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
        var model = config.DefaultModel ?? "gpt-4o";

        _logger.LogDebug("Created Azure OpenAI client at {Endpoint}, model={Model}", endpoint, model);
        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient CreateCustomOpenAIClient(ProviderConfig config)
    {
        var baseUrl = config.BaseUrl ?? throw new ArgumentException("Custom provider requires a base URL.");
        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
        var model = config.DefaultModel ?? "default";

        _logger.LogDebug("Created custom OpenAI-compatible client at {BaseUrl}, model={Model}", baseUrl, model);
        return client.GetChatClient(model).AsIChatClient();
    }
}
