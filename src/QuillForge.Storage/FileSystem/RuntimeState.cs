namespace QuillForge.Storage.FileSystem;

public sealed class RuntimeState
{
    public string? LastMode { get; set; }
    public string? LastProject { get; set; }
    public string? LastFile { get; set; }
    public string? LastCharacter { get; set; }
    public Guid? LastSessionId { get; set; }
}
