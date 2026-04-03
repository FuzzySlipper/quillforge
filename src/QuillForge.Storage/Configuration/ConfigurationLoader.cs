using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Storage.Configuration;

/// <summary>
/// Loads and validates AppConfig from YAML configuration files.
/// </summary>
public sealed class ConfigurationLoader
{
    private readonly ILogger<ConfigurationLoader> _logger;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public ConfigurationLoader(ILogger<ConfigurationLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads config from the given YAML file path. Returns defaults if file doesn't exist.
    /// </summary>
    public AppConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger.LogInformation("Config file not found at {Path}, using defaults", configPath);
            return new AppConfig();
        }

        try
        {
            var yaml = File.ReadAllText(configPath);
            var config = Deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
            _logger.LogInformation("Loaded configuration from {Path}", configPath);
            Validate(config);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}, using defaults", configPath);
            return new AppConfig();
        }
    }

    /// <summary>
    /// Validates config values and logs warnings for issues.
    /// </summary>
    public void Validate(AppConfig config)
    {
        if (config.Forge.ReviewPassThreshold is < 1 or > 10)
        {
            _logger.LogWarning(
                "forge.review_pass_threshold ({Value}) should be between 1 and 10",
                config.Forge.ReviewPassThreshold);
        }

        if (config.Forge.MaxRevisions < 0)
        {
            _logger.LogWarning(
                "forge.max_revisions ({Value}) should not be negative",
                config.Forge.MaxRevisions);
        }

        if (config.Forge.StageTimeoutMinutes < 1)
        {
            _logger.LogWarning(
                "forge.stage_timeout_minutes ({Value}) should be at least 1",
                config.Forge.StageTimeoutMinutes);
        }

        if (config.WebSearch.Enabled && string.IsNullOrEmpty(config.WebSearch.SearxngUrl)
            && config.WebSearch.Provider == "searxng")
        {
            _logger.LogWarning("web_search is enabled with searxng provider but searxng_url is not set");
        }

        if (config.Persona.MaxTokens < 100)
        {
            _logger.LogWarning(
                "persona.max_tokens ({Value}) seems too low",
                config.Persona.MaxTokens);
        }

        // Agent budget validation
        var agents = config.Agents;
        if (agents.Orchestrator.MaxToolRounds < 1)
            _logger.LogWarning("agents.orchestrator.max_tool_rounds ({Value}) must be at least 1", agents.Orchestrator.MaxToolRounds);
        if (agents.NarrativeDirector.MaxTokens < 256)
            _logger.LogWarning("agents.narrative_director.max_tokens ({Value}) seems too low", agents.NarrativeDirector.MaxTokens);
        if (agents.Librarian.MaxTokens < 256)
            _logger.LogWarning("agents.librarian.max_tokens ({Value}) seems too low", agents.Librarian.MaxTokens);
        if (agents.ProseWriter.MaxTokens < 256)
            _logger.LogWarning("agents.prose_writer.max_tokens ({Value}) seems too low", agents.ProseWriter.MaxTokens);
        if (agents.Council.Temperature is < 0 or > 2)
            _logger.LogWarning("agents.council.temperature ({Value}) should be between 0 and 2", agents.Council.Temperature);

        // Timeout validation
        var timeouts = config.Timeouts;
        if (timeouts.ToolExecutionSeconds < 5)
            _logger.LogWarning("timeouts.tool_execution_seconds ({Value}) seems too low, tools may time out prematurely", timeouts.ToolExecutionSeconds);
        if (timeouts.ProviderHttpSeconds < 1)
            _logger.LogWarning("timeouts.provider_http_seconds ({Value}) must be at least 1", timeouts.ProviderHttpSeconds);
        if (timeouts.UpdateCheckHours < 1)
            _logger.LogWarning("timeouts.update_check_hours ({Value}) must be at least 1", timeouts.UpdateCheckHours);
    }

    /// <summary>
    /// Serializes an AppConfig to a YAML string.
    /// Use this instead of hand-building YAML to ensure all fields round-trip correctly.
    /// </summary>
    public static string Serialize(AppConfig config)
    {
        return Serializer.Serialize(config);
    }

    /// <summary>
    /// Creates a default config.yaml at the given path.
    /// </summary>
    public void WriteDefaults(string configPath)
    {
        var yaml = Serialize(new AppConfig());
        File.WriteAllText(configPath, yaml);
        _logger.LogInformation("Created default config at {Path}", configPath);
    }
}
