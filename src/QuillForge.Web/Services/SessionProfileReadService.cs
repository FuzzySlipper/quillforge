using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Services;

public interface ISessionProfileReadService
{
    Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default);
    Task<ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default);
    Task<PreparedInteractiveRequest> PrepareInteractiveRequestAsync(
        Guid? sessionId,
        PrepareInteractiveRequestOptions options,
        CancellationToken ct = default);
}

internal static class SessionProfileHydration
{
    public static string RequireProfileId(ProfileState profile)
        => profile.ProfileId ?? throw new InvalidOperationException("Hydrated session profile id was unexpectedly null.");

    public static string RequireActiveConductor(ProfileState profile)
        => profile.ActiveConductor ?? throw new InvalidOperationException("Hydrated session conductor was unexpectedly null.");

    public static string RequireActiveLoreSet(ProfileState profile)
        => profile.ActiveLoreSet ?? throw new InvalidOperationException("Hydrated session lore set was unexpectedly null.");

    public static string RequireActiveNarrativeRules(ProfileState profile)
        => profile.ActiveNarrativeRules ?? throw new InvalidOperationException("Hydrated session narrative rules were unexpectedly null.");

    public static string RequireActiveWritingStyle(ProfileState profile)
        => profile.ActiveWritingStyle ?? throw new InvalidOperationException("Hydrated session writing style was unexpectedly null.");
}

public sealed record SessionProfileReadView
{
    public required SessionState SessionState { get; init; }
    public required string DefaultProfileId { get; init; }
    public required string ActiveProfileId { get; init; }
    public required string ActiveConductor { get; init; }
    public required string ActiveLoreSet { get; init; }
    public required string ActiveNarrativeRules { get; init; }
    public required string ActiveWritingStyle { get; init; }
    public string? ActiveAiCharacter { get; init; }
    public string? ActiveUserCharacter { get; init; }
}

public sealed record PrepareInteractiveRequestOptions
{
    public string? RequestedConductor { get; init; }
    public string? LastAssistantResponse { get; init; }
    public bool ResolvePortraits { get; init; }
}

public sealed record PreparedInteractiveRequest
{
    public required SessionProfileReadView ProfileView { get; init; }
    public required InteractiveSessionContext SessionContext { get; init; }
    public required AgentContext AgentContext { get; init; }
    public required string Conductor { get; init; }
    public string? AssistantPortraitUrl { get; init; }
    public string? UserPortraitUrl { get; init; }
}

public sealed class SessionProfileReadService : ISessionProfileReadService
{
    private readonly ISessionStateService _runtimeService;
    private readonly IProfileConfigService _profileService;
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ICharacterCardStore _characterCardStore;
    private readonly IConductorStore _conductorStore;
    private readonly ILoreStore _loreStore;
    private readonly INarrativeRulesStore _narrativeRulesStore;
    private readonly IWritingStyleStore _writingStyleStore;
    private readonly ILogger<SessionProfileReadService> _logger;

    public SessionProfileReadService(
        ISessionStateService runtimeService,
        IProfileConfigService profileService,
        IInteractiveSessionContextService sessionContextService,
        ICharacterCardStore characterCardStore,
        IConductorStore conductorStore,
        ILoreStore loreStore,
        INarrativeRulesStore narrativeRulesStore,
        IWritingStyleStore writingStyleStore,
        ILogger<SessionProfileReadService> logger)
    {
        _runtimeService = runtimeService;
        _profileService = profileService;
        _sessionContextService = sessionContextService;
        _characterCardStore = characterCardStore;
        _conductorStore = conductorStore;
        _loreStore = loreStore;
        _narrativeRulesStore = narrativeRulesStore;
        _writingStyleStore = writingStyleStore;
        _logger = logger;
    }

    public async Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        var sessionState = await _runtimeService.LoadViewAsync(sessionId, ct);
        var defaultProfileId = await _profileService.GetDefaultProfileIdAsync(ct);

        var view = new SessionProfileReadView
        {
            SessionState = sessionState,
            DefaultProfileId = defaultProfileId,
            ActiveProfileId = SessionProfileHydration.RequireProfileId(sessionState.Profile),
            ActiveConductor = SessionProfileHydration.RequireActiveConductor(sessionState.Profile),
            ActiveLoreSet = SessionProfileHydration.RequireActiveLoreSet(sessionState.Profile),
            ActiveNarrativeRules = SessionProfileHydration.RequireActiveNarrativeRules(sessionState.Profile),
            ActiveWritingStyle = SessionProfileHydration.RequireActiveWritingStyle(sessionState.Profile),
            ActiveAiCharacter = sessionState.Roleplay.ActiveAiCharacter,
            ActiveUserCharacter = sessionState.Roleplay.ActiveUserCharacter,
        };

        _logger.LogInformation(
            "Loaded session profile read view: session={SessionId} profileId={ProfileId} conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle} aiCharacter={AiCharacter} userCharacter={UserCharacter}",
            sessionId,
            view.ActiveProfileId,
            view.ActiveConductor,
            view.ActiveLoreSet,
            view.ActiveNarrativeRules,
            view.ActiveWritingStyle,
            view.ActiveAiCharacter,
            view.ActiveUserCharacter);

        return view;
    }

    public async Task<PreparedInteractiveRequest> PrepareInteractiveRequestAsync(
        Guid? sessionId,
        PrepareInteractiveRequestOptions options,
        CancellationToken ct = default)
    {
        var view = await LoadAsync(sessionId, ct);
        var sessionContext = await _sessionContextService.BuildAsync(view.SessionState, ct);
        var conductor = NormalizeChoice(options.RequestedConductor) ?? view.ActiveConductor;
        var resolvedSessionId = view.SessionState.SessionId ?? Guid.CreateVersion7();

        string? assistantPortraitUrl = null;
        string? userPortraitUrl = null;
        if (options.ResolvePortraits)
        {
            assistantPortraitUrl = await ResolvePortraitUrlAsync(view.ActiveAiCharacter, ct);
            userPortraitUrl = await ResolvePortraitUrlAsync(view.ActiveUserCharacter, ct);
        }

        var agentContext = new AgentContext
        {
            SessionId = resolvedSessionId,
            ActiveMode = view.SessionState.Mode.ActiveModeName,
            ActiveLoreSet = view.ActiveLoreSet,
            ActiveNarrativeRules = view.ActiveNarrativeRules,
            ActiveWritingStyle = view.ActiveWritingStyle,
            SessionContext = sessionContext,
            LastAssistantResponse = NormalizeChoice(options.LastAssistantResponse),
        };

        _logger.LogInformation(
            "Prepared interactive request context: session={SessionId} mode={Mode} conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            resolvedSessionId,
            view.SessionState.Mode.ActiveModeName,
            conductor,
            view.ActiveLoreSet,
            view.ActiveNarrativeRules,
            view.ActiveWritingStyle);

        return new PreparedInteractiveRequest
        {
            ProfileView = view,
            SessionContext = sessionContext,
            AgentContext = agentContext,
            Conductor = conductor,
            AssistantPortraitUrl = assistantPortraitUrl,
            UserPortraitUrl = userPortraitUrl,
        };
    }

    public async Task<ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
    {
        var view = await LoadAsync(sessionId, ct);
        var profileIds = await _profileService.ListAsync(ct);
        var conductors = await _conductorStore.ListAsync(ct);
        var loreSets = await _loreStore.ListLoreSetsAsync(ct);
        var narrativeRules = await _narrativeRulesStore.ListAsync(ct);
        var styles = await _writingStyleStore.ListAsync(ct);

        var response = new ProfilesResponse
        {
            ProfileIds = profileIds,
            DefaultProfileId = view.DefaultProfileId,
            ActiveProfileId = view.ActiveProfileId,
            Conductors = conductors,
            LoreSets = loreSets,
            NarrativeRules = narrativeRules,
            WritingStyles = styles,
            ActiveConductor = view.ActiveConductor,
            ActiveLore = view.ActiveLoreSet,
            ActiveNarrativeRules = view.ActiveNarrativeRules,
            ActiveWritingStyle = view.ActiveWritingStyle,
        };

        _logger.LogInformation(
            "Built profiles response for session {SessionId}: profileCount={ProfileCount} conductorCount={ConductorCount} loreSetCount={LoreCount}",
            sessionId,
            response.ProfileIds.Count,
            response.Conductors.Count,
            response.LoreSets.Count);

        return response;
    }

    private async Task<string?> ResolvePortraitUrlAsync(string? characterName, CancellationToken ct)
    {
        var normalizedCharacterName = NormalizeChoice(characterName);
        if (normalizedCharacterName is null)
        {
            return null;
        }

        var card = await _characterCardStore.LoadAsync(normalizedCharacterName, ct);
        return card?.Portrait is not null ? $"/content/character-cards/{card.Portrait}" : null;
    }

    private static string? NormalizeChoice(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
