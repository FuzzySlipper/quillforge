using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Web.Services;

public interface ICharacterCardCommandService
{
    Task<bool> DeleteAsync(string fileName, CancellationToken ct = default);
    Task<SessionMutationResult<SessionState>> ActivateAsync(
        ActivateCharacterCardsCommand command,
        CancellationToken ct = default);
}

public sealed record ActivateCharacterCardsCommand(
    Guid? SessionId,
    bool HasAiCharacterSelection,
    string? AiCharacter,
    bool HasUserCharacterSelection,
    string? UserCharacter);

public sealed class CharacterCardCommandService : ICharacterCardCommandService
{
    private readonly ICharacterCardStore _store;
    private readonly ISessionStateService _runtimeService;
    private readonly ISessionBootstrapService _bootstrapService;
    private readonly ISessionLifecycleService _lifecycleService;
    private readonly ILogger<CharacterCardCommandService> _logger;

    public CharacterCardCommandService(
        ICharacterCardStore store,
        ISessionStateService runtimeService,
        ISessionBootstrapService bootstrapService,
        ISessionLifecycleService lifecycleService,
        ILogger<CharacterCardCommandService> logger)
    {
        _store = store;
        _runtimeService = runtimeService;
        _bootstrapService = bootstrapService;
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting character card through command service: fileName={FileName}", fileName);
        return await _store.DeleteAsync(fileName, ct);
    }

    public async Task<SessionMutationResult<SessionState>> ActivateAsync(
        ActivateCharacterCardsCommand command,
        CancellationToken ct = default)
    {
        var sessionId = command.SessionId;
        Guid? createdSessionId = null;

        if (!sessionId.HasValue)
        {
            var tree = await _bootstrapService.CreateAsync(
                new CreateSessionCommand
                {
                    Name = "New Session",
                },
                ct);
            sessionId = tree.SessionId;
            createdSessionId = tree.SessionId;
        }

        _logger.LogInformation(
            "Activating character cards through command service: session={SessionId} hasAi={HasAiSelection} ai={AiCharacter} hasUser={HasUserSelection} user={UserCharacter}",
            sessionId,
            command.HasAiCharacterSelection,
            command.AiCharacter,
            command.HasUserCharacterSelection,
            command.UserCharacter);

        var result = await _runtimeService.SetRoleplayAsync(
            sessionId,
            new SetSessionRoleplayCommand(
                command.HasAiCharacterSelection,
                command.AiCharacter,
                command.HasUserCharacterSelection,
                command.UserCharacter),
            ct);

        if ((result.Status == SessionMutationStatus.Busy || result.Status == SessionMutationStatus.Invalid)
            && createdSessionId.HasValue)
        {
            await _lifecycleService.DeleteAsync(createdSessionId.Value, ct);
        }

        return result;
    }
}
