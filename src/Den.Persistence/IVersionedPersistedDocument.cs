using System.Text.Json.Nodes;

namespace Den.Persistence;

/// <summary>
/// Optional extension point for persisted documents that need explicit schema
/// versioning and raw-content migrations for breaking shape changes.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public interface IVersionedPersistedDocument<T> : IPersistedDocument<T>
{
    /// <summary>
    /// The latest schema version written by this document definition.
    /// </summary>
    int CurrentVersion { get; }

    /// <summary>
    /// The assumed version when the serialized document does not yet carry an
    /// explicit version field. Defaults to 1 for legacy unversioned files.
    /// </summary>
    int InitialVersion => 1;

    /// <summary>
    /// The persisted field name used to store the schema version.
    /// Override when a document wants a different on-disk naming convention.
    /// </summary>
    string VersionFieldName => "schemaVersion";

    /// <summary>
    /// Migrates the serialized document from <paramref name="fromVersion"/> to
    /// <paramref name="fromVersion"/> + 1 by mutating the raw object graph.
    /// Implementations should throw for unsupported versions.
    /// </summary>
    void MigrateOneVersion(JsonObject document, int fromVersion);
}
