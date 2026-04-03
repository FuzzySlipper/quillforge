using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IInteractiveSessionContextService
{
    Task<InteractiveSessionContext> BuildAsync(
        SessionRuntimeState state,
        CancellationToken ct = default);

    Task<InteractiveSessionContext> LoadAsync(
        Guid? sessionId,
        CancellationToken ct = default);
}
