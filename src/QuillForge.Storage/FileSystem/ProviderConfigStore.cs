using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Public DTO for provider configs. Contains plaintext API key for use by the Web layer.
/// Storage has no access to QuillForge.Providers types, so this is the bridge type.
/// </summary>
public sealed class ProviderConfigDto
{
    public required string Alias { get; init; }
    public required string Type { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? DefaultModel { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraSettings { get; init; }
}

/// <summary>
/// Persists provider configurations to data/providers.json with encrypted API keys.
/// </summary>
public sealed class ProviderConfigStore
{
    private readonly string _filePath;
    private readonly EncryptedKeyStore _keyStore;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<ProviderConfigStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ProviderConfigStore(
        string contentRoot,
        EncryptedKeyStore keyStore,
        AtomicFileWriter writer,
        ILogger<ProviderConfigStore> logger)
    {
        _filePath = Path.Combine(contentRoot, "data", "providers.json");
        _keyStore = keyStore;
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// Loads all provider configs from disk. Decrypts API keys.
    /// Returns empty list if file is missing or corrupt.
    /// </summary>
    public List<ProviderConfigDto> Load()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            var stored = JsonSerializer.Deserialize<StoredFile>(json, JsonOptions);
            if (stored?.Providers is null)
                return [];

            var result = new List<ProviderConfigDto>();
            foreach (var sp in stored.Providers)
            {
                string? apiKey = null;
                if (!string.IsNullOrEmpty(sp.EncryptedApiKey))
                {
                    try
                    {
                        apiKey = _keyStore.Decrypt(sp.EncryptedApiKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key for provider '{Alias}'", sp.Alias);
                    }
                }

                result.Add(new ProviderConfigDto
                {
                    Alias = sp.Alias,
                    Type = sp.Type,
                    ApiKey = apiKey,
                    BaseUrl = sp.BaseUrl,
                    DefaultModel = sp.DefaultModel,
                    ExtraSettings = sp.ExtraSettings,
                });
            }

            _logger.LogInformation("Loaded {Count} providers from {Path}", result.Count, _filePath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load providers from {Path}, returning empty list", _filePath);
            return [];
        }
    }

    /// <summary>
    /// Saves all provider configs to disk. Encrypts API keys before writing.
    /// </summary>
    public async Task SaveAsync(List<ProviderConfigDto> configs, CancellationToken ct = default)
    {
        var stored = new StoredFile
        {
            Providers = configs.Select(c => new StoredProvider
            {
                Alias = c.Alias,
                Type = c.Type,
                EncryptedApiKey = !string.IsNullOrEmpty(c.ApiKey) ? _keyStore.Encrypt(c.ApiKey) : null,
                BaseUrl = c.BaseUrl,
                DefaultModel = c.DefaultModel,
                ExtraSettings = c.ExtraSettings,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        await _writer.WriteAsync(_filePath, json, ct);
        _logger.LogDebug("Saved {Count} providers to {Path}", configs.Count, _filePath);
    }

    // --- Internal serialization models ---

    private sealed class StoredFile
    {
        public List<StoredProvider> Providers { get; set; } = [];
    }

    private sealed class StoredProvider
    {
        public required string Alias { get; set; }
        public required string Type { get; set; }
        public string? EncryptedApiKey { get; set; }
        public string? BaseUrl { get; set; }
        public string? DefaultModel { get; set; }
        public IReadOnlyDictionary<string, string>? ExtraSettings { get; set; }
    }
}
