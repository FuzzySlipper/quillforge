using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionBootstrapService : ISessionBootstrapService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISessionRuntimeStore _runtimeStore;
    private readonly IProfileConfigService _profileService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionBootstrapService> _logger;

    public SessionBootstrapService(
        ISessionStore sessionStore,
        ISessionRuntimeStore runtimeStore,
        IProfileConfigService profileService,
        ILoggerFactory loggerFactory,
        ILogger<SessionBootstrapService> logger)
    {
        _sessionStore = sessionStore;
        _runtimeStore = runtimeStore;
        _profileService = profileService;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default)
    {
        var runtimeProfile = await _profileService.BuildSessionProfileStateAsync(command.ProfileId, ct);
        var resolvedProfile = await _profileService.LoadResolvedAsync(command.ProfileId, ct);
        var sessionId = command.SessionId ?? Guid.CreateVersion7();
        var sessionName = string.IsNullOrWhiteSpace(command.Name) ? "New Session" : command.Name.Trim();

        var tree = new ConversationTree(
            sessionId,
            sessionName,
            _loggerFactory.CreateLogger<ConversationTree>());

        await _sessionStore.SaveAsync(tree, ct);

        try
        {
            await _runtimeStore.SaveAsync(new SessionRuntimeState
            {
                SessionId = sessionId,
                Profile = runtimeProfile,
                Roleplay = new RoleplayRuntimeState
                {
                    ActiveAiCharacter = resolvedProfile.Config.Roleplay.AiCharacter,
                    ActiveUserCharacter = resolvedProfile.Config.Roleplay.UserCharacter,
                },
            }, ct);
        }
        catch
        {
            try
            {
                await _sessionStore.DeleteAsync(sessionId, ct);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(
                    cleanupEx,
                    "Failed to clean up session tree after runtime bootstrap failure: session={SessionId}",
                    sessionId);
            }

            throw;
        }

        _logger.LogInformation(
            "Created session bootstrap: session={SessionId} name={Name} profileId={ProfileId} aiCharacter={AiCharacter} userCharacter={UserCharacter}",
            sessionId,
            sessionName,
            runtimeProfile.ProfileId,
            resolvedProfile.Config.Roleplay.AiCharacter,
            resolvedProfile.Config.Roleplay.UserCharacter);

        return tree;
    }
}
