using Den.Persistence;
using QuillForge.Core;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persisted document definition for legacy runtime state (data/runtime-state.json).
/// </summary>
public sealed class RuntimeStateDocument : PersistedDocumentBase<RuntimeState>
{
    public override string RelativePath => ContentPaths.RuntimeStateFile;

    public override RuntimeState CreateDefault() => new();
}
