using Microsoft.Extensions.Logging;
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
}
