using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GamePersonaPromptService : IGamePersonaPromptService
{
    private const string DefaultSeedContent = "Describe the reusable persona, voice, goals, and roleplay boundaries for this game agent.";
    private readonly IGamePersonaPromptStore _store;
    private readonly ILogger<GamePersonaPromptService> _logger;

    public GamePersonaPromptService(
        IGamePersonaPromptStore store,
        ILogger<GamePersonaPromptService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListUserPromptsAsync(CancellationToken ct = default) =>
        _store.ListAsync(ct);

    public Task<GameUserPersonaPromptDocument> CreateForEditAsync(string? basePromptName, string? seedContent, CancellationToken ct = default)
    {
        var baseName = string.IsNullOrWhiteSpace(basePromptName)
            ? $"persona-{DateTimeOffset.UtcNow:yyyyMMdd}"
            : basePromptName.Trim();
        var content = string.IsNullOrWhiteSpace(seedContent)
            ? DefaultSeedContent
            : seedContent.Trim();
        return _store.CreateUniqueAsync(baseName, content, ct);
    }

    public Task<GameUserPersonaPromptDocument?> TryOpenUserPromptAsync(string promptName, CancellationToken ct = default) =>
        _store.TryLoadAsync(promptName, ct);

    public Task SaveUserPromptAsync(string promptName, string content, CancellationToken ct = default) =>
        _store.SaveAsync(promptName, content, ct);

    public async Task<GameResolvedPersonaPrompt> ResolveAsync(GamePersonaPromptSelection? selection, CancellationToken ct = default)
    {
        var requested = NormalizeSelection(selection);
        if (!requested.IsUserPrompt)
        {
            return new GameResolvedPersonaPrompt
            {
                Content = null,
                Selection = GamePersonaPromptSelection.None,
            };
        }

        var document = await _store.TryLoadAsync(requested.UserPromptName!, ct);
        if (document is not null && !string.IsNullOrWhiteSpace(document.Content))
        {
            return new GameResolvedPersonaPrompt
            {
                Content = document.Content.Trim(),
                Selection = requested,
            };
        }

        var reason = document is null ? "persona_prompt_missing" : "persona_prompt_blank";
        _logger.LogWarning(
            "Game persona prompt fell back to no persona: prompt={PromptName} reason={Reason}",
            requested.UserPromptName,
            reason);
        return new GameResolvedPersonaPrompt
        {
            Content = null,
            Selection = GamePersonaPromptSelection.None,
            UsedFallback = true,
            FallbackReason = reason,
        };
    }

    private static GamePersonaPromptSelection NormalizeSelection(GamePersonaPromptSelection? selection)
    {
        if (selection is null || selection.Source != GamePersonaPromptSource.User)
        {
            return GamePersonaPromptSelection.None;
        }

        var promptName = string.IsNullOrWhiteSpace(selection.UserPromptName)
            ? null
            : selection.UserPromptName.Trim();
        return promptName is null
            ? GamePersonaPromptSelection.None
            : GamePersonaPromptSelection.ForUserPrompt(promptName);
    }
}
