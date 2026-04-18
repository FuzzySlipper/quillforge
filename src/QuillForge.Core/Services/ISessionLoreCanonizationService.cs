using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface ISessionLoreCanonizationService
{
    Task<SessionMutationResult<LoreCanonizationProposalGeneratedEvent>> GenerateProposalAsync(
        Guid? sessionId,
        GenerateLoreCanonizationProposalCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<LoreCanonizationAppliedEvent>> ApplyProposalAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<LoreCanonizationDiscardedEvent>> DiscardProposalAsync(
        Guid? sessionId,
        CancellationToken ct = default);
}
