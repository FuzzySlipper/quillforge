using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionBootstrapService : ISessionBootstrapService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISessionStateStore _runtimeStore;
    private readonly IProfileConfigService _profileService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionBootstrapService> _logger;

    public SessionBootstrapService(
        ISessionStore sessionStore,
        ISessionStateStore runtimeStore,
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
        var resolvedProfile = await _profileService.LoadResolvedAsync(command.ProfileId, ct);
        var runtimeProfile = new ProfileState
        {
            ProfileId = resolvedProfile.ProfileId,
        };
        var sessionId = command.SessionId ?? Guid.CreateVersion7();
        var sessionName = string.IsNullOrWhiteSpace(command.Name) ? "New Session" : command.Name.Trim();

        var tree = new ConversationTree(
            sessionId,
            sessionName,
            _loggerFactory.CreateLogger<ConversationTree>());

        await _sessionStore.SaveAsync(tree, ct);

        try
        {
            await _runtimeStore.SaveAsync(new SessionState
            {
                SessionId = sessionId,
                Profile = runtimeProfile,
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
            "Created session bootstrap: session={SessionId} name={Name} profileId={ProfileId} seededRoleplayFromProfileDefaults={SeededRoleplayFromProfileDefaults}",
            sessionId,
            sessionName,
            runtimeProfile.ProfileId,
            false);

        return tree;
    }
}
