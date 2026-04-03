using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Den.Persistence;

/// <summary>
/// A file-backed persisted document store using JSON serialization
/// with camelCase naming convention.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public class JsonPersistedDocumentStore<T> : FilePersistedDocumentStore<T>
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly JsonSerializerOptions _options;

    public JsonPersistedDocumentStore(
        IPersistedDocument<T> document,
        string contentRoot,
        AtomicFileWriter writer,
        ILogger logger,
        JsonSerializerOptions? options = null)
        : base(document, contentRoot, writer, logger)
    {
        _options = options ?? DefaultOptions;
    }

    /// <inheritdoc />
    protected override T? Deserialize(string content)
        => JsonSerializer.Deserialize<T>(content, _options);

    /// <inheritdoc />
    protected override string Serialize(T value)
        => JsonSerializer.Serialize(value, _options);

    /// <inheritdoc />
    protected override JsonObject ParseRootObject(string content)
    {
        var node = JsonNode.Parse(content);
        return node as JsonObject
            ?? throw new InvalidOperationException("Persisted JSON document root must be an object.");
    }

    /// <inheritdoc />
    protected override string SerializeRootObject(JsonObject root)
        => root.ToJsonString(_options);
}
