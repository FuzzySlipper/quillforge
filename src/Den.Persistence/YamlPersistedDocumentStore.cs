using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Den.Persistence;

/// <summary>
/// A file-backed persisted document store using YAML serialization
/// with snake_case naming convention.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public class YamlPersistedDocumentStore<T> : FilePersistedDocumentStore<T>
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public YamlPersistedDocumentStore(
        IPersistedDocument<T> document,
        string contentRoot,
        AtomicFileWriter writer,
        ILogger logger)
        : base(document, contentRoot, writer, logger)
    {
    }

    /// <inheritdoc />
    protected override T? Deserialize(string content)
        => Deserializer.Deserialize<T>(content);

    /// <inheritdoc />
    protected override string Serialize(T value)
        => Serializer.Serialize(value);

    /// <inheritdoc />
    protected override JsonObject ParseRootObject(string content)
    {
        var deserialized = Deserializer.Deserialize<object>(content);
        var node = ToJsonNode(deserialized);
        return node as JsonObject
            ?? throw new InvalidOperationException("Persisted YAML document root must be an object.");
    }

    /// <inheritdoc />
    protected override string SerializeRootObject(JsonObject root)
        => Serializer.Serialize(ToPlainObject(root));

    private static JsonNode? ToJsonNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IDictionary<object, object> objectDictionary)
        {
            var result = new JsonObject();
            foreach (var pair in objectDictionary)
            {
                var key = pair.Key?.ToString()
                    ?? throw new InvalidOperationException("Persisted YAML document keys cannot be null.");
                result[key] = ToJsonNode(pair.Value);
            }

            return result;
        }

        if (value is IDictionary<string, object> stringDictionary)
        {
            var result = new JsonObject();
            foreach (var pair in stringDictionary)
            {
                result[pair.Key] = ToJsonNode(pair.Value);
            }

            return result;
        }

        if (value is IEnumerable<object> items && value is not string)
        {
            var array = new JsonArray();
            foreach (var item in items)
            {
                array.Add(ToJsonNode(item));
            }

            return array;
        }

        return JsonValue.Create(value);
    }

    private static object? ToPlainObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in obj)
            {
                result[pair.Key] = ToPlainObject(pair.Value);
            }

            return result;
        }

        if (node is JsonArray array)
        {
            var result = new List<object?>();
            foreach (var item in array)
            {
                result.Add(ToPlainObject(item));
            }

            return result;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }

            return value.ToJsonString();
        }

        throw new InvalidOperationException($"Unsupported JSON node type: {node.GetType().Name}");
    }
}
