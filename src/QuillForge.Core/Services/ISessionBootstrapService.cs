using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Creates a persisted session unit with both conversation history and seeded
/// session runtime state.
/// </summary>
public interface ISessionBootstrapService
{
    Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default);
}

public sealed record CreateSessionCommand
{
    public Guid? SessionId { get; init; }
    public string Name { get; init; } = "New Session";
    public string? ProfileId { get; init; }
}
