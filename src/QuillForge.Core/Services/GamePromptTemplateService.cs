using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GamePromptTemplateService : IGamePromptTemplateService
{
    private readonly IGamePromptTemplateStore _store;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly ILogger<GamePromptTemplateService> _logger;

    public GamePromptTemplateService(
        IGamePromptTemplateStore store,
        GameModuleRegistry moduleRegistry,
        ILogger<GamePromptTemplateService> logger)
    {
        _store = store;
        _moduleRegistry = moduleRegistry;
        _logger = logger;
    }

    public Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListUserPromptsAsync(string moduleId, CancellationToken ct = default) =>
        _store.ListAsync(moduleId, ct);

    public async Task<GameUserPromptTemplateDocument> CopyDefaultForEditAsync(string moduleId, CancellationToken ct = default)
    {
        var module = FindModule(moduleId)
            ?? throw new ArgumentException($"Game module '{moduleId}' is not registered.", nameof(moduleId));
        var defaultPrompt = BuildDefaultPrompt(module);
        var baseName = $"{NormalizePromptName(module.Descriptor.ModuleId.Value)}-prompt-{DateTimeOffset.UtcNow:yyyyMMdd}";
        return await _store.CreateUniqueAsync(module.Descriptor.ModuleId.Value, baseName, defaultPrompt, ct);
    }

    public Task<GameUserPromptTemplateDocument?> TryOpenUserPromptAsync(string moduleId, string promptName, CancellationToken ct = default) =>
        _store.TryLoadAsync(moduleId, promptName, ct);

    public Task SaveUserPromptAsync(string moduleId, string promptName, string content, CancellationToken ct = default) =>
        _store.SaveAsync(moduleId, promptName, content, ct);

    public async Task<GameResolvedPromptTemplate> ResolveAsync(
        IGameModule module,
        GamePromptTemplateSelection? selection,
        CancellationToken ct = default)
    {
        var requested = NormalizeSelection(selection);
        if (requested.IsUserPrompt)
        {
            var document = await _store.TryLoadAsync(module.Descriptor.ModuleId.Value, requested.UserPromptName!, ct);
            if (document is not null && !string.IsNullOrWhiteSpace(document.Content))
            {
                return new GameResolvedPromptTemplate
                {
                    Content = document.Content.Trim(),
                    Selection = requested,
                };
            }

            var reason = document is null ? "user_prompt_missing" : "user_prompt_blank";
            _logger.LogWarning(
                "Game user prompt template fell back to module default: module={ModuleId} prompt={PromptName} reason={Reason}",
                module.Descriptor.ModuleId.Value,
                requested.UserPromptName,
                reason);
            return new GameResolvedPromptTemplate
            {
                Content = BuildDefaultPrompt(module),
                Selection = GamePromptTemplateSelection.Default,
                UsedFallback = true,
                FallbackReason = reason,
            };
        }

        return new GameResolvedPromptTemplate
        {
            Content = BuildDefaultPrompt(module),
            Selection = GamePromptTemplateSelection.Default,
        };
    }

    public static string BuildDefaultPrompt(IGameModule module)
    {
        var prompts = module.GetPromptAssets()
            .Where(asset => asset.Kind == GamePromptAssetKind.ParticipantInstructions)
            .Select(asset => asset.Content.Trim())
            .Where(content => content.Length > 0)
            .ToArray();
        return string.Join("\n\n", prompts);
    }

    private IGameModule? FindModule(string moduleId)
    {
        var normalized = NormalizePromptName(moduleId);
        return _moduleRegistry.Modules
            .Where(module => string.Equals(module.Descriptor.ModuleId.Value, normalized, StringComparison.Ordinal))
            .OrderByDescending(module => module.Descriptor.ModuleVersion.Value, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static GamePromptTemplateSelection NormalizeSelection(GamePromptTemplateSelection? selection)
    {
        if (selection is null || selection.Source != GamePromptTemplateSource.User)
        {
            return GamePromptTemplateSelection.Default;
        }

        var promptName = string.IsNullOrWhiteSpace(selection.UserPromptName)
            ? null
            : selection.UserPromptName.Trim();
        return promptName is null
            ? GamePromptTemplateSelection.Default
            : GamePromptTemplateSelection.ForUserPrompt(promptName);
    }

    private static string NormalizePromptName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A prompt/module name is required.", nameof(value));
        }

        return value.Trim();
    }
}
