using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GamePromptTemplateServiceTests
{
    [Fact]
    public async Task ResolveAsync_DefaultSelectionUsesBundledModuleParticipantInstructions()
    {
        var fixture = CreateFixture();

        var resolved = await fixture.Service.ResolveAsync(fixture.Module, GamePromptTemplateSelection.Default);

        Assert.False(resolved.UsedFallback);
        Assert.Equal(GamePromptTemplateSource.Default, resolved.Selection.Source);
        Assert.Contains("Bundled participant instructions", resolved.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_UserSelectionUsesUserOwnedPromptWhenPresent()
    {
        var fixture = CreateFixture();
        await fixture.Store.SaveAsync(TestGameModule.ModuleId, "custom-prompt", "User prompt content.");

        var resolved = await fixture.Service.ResolveAsync(
            fixture.Module,
            GamePromptTemplateSelection.ForUserPrompt("custom-prompt"));

        Assert.False(resolved.UsedFallback);
        Assert.Equal(GamePromptTemplateSource.User, resolved.Selection.Source);
        Assert.Equal("custom-prompt", resolved.Selection.UserPromptName);
        Assert.Equal("User prompt content.", resolved.Content);
    }

    [Fact]
    public async Task ResolveAsync_MissingOrBlankUserPromptFallsBackToBundledDefault()
    {
        var fixture = CreateFixture();
        await fixture.Store.SaveAsync(TestGameModule.ModuleId, "blank-prompt", "   ");

        var missing = await fixture.Service.ResolveAsync(
            fixture.Module,
            GamePromptTemplateSelection.ForUserPrompt("missing-prompt"));
        var blank = await fixture.Service.ResolveAsync(
            fixture.Module,
            GamePromptTemplateSelection.ForUserPrompt("blank-prompt"));

        Assert.True(missing.UsedFallback);
        Assert.Equal("user_prompt_missing", missing.FallbackReason);
        Assert.Contains("Bundled participant instructions", missing.Content, StringComparison.Ordinal);
        Assert.True(blank.UsedFallback);
        Assert.Equal("user_prompt_blank", blank.FallbackReason);
        Assert.Contains("Bundled participant instructions", blank.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyDefaultForEdit_CreatesUserOwnedCopyAndOpeningExistingDoesNotDuplicate()
    {
        var fixture = CreateFixture();

        var copy = await fixture.Service.CopyDefaultForEditAsync(TestGameModule.ModuleId);
        var opened = await fixture.Service.TryOpenUserPromptAsync(TestGameModule.ModuleId, copy.Name);
        var listed = await fixture.Service.ListUserPromptsAsync(TestGameModule.ModuleId);

        Assert.NotNull(opened);
        Assert.Equal(copy.Name, opened.Name);
        Assert.Equal(copy.Content, opened.Content);
        Assert.Contains(TestGameModule.ModuleId, copy.Name, StringComparison.Ordinal);
        Assert.Contains("prompt", copy.Name, StringComparison.Ordinal);
        Assert.Single(listed);
    }

    private static Fixture CreateFixture()
    {
        var module = new TestGameModule();
        var registry = new GameModuleRegistry();
        Assert.True(registry.Register(module).IsValid);
        var store = new InMemoryGamePromptTemplateStore();
        var service = new GamePromptTemplateService(store, registry, NullLogger<GamePromptTemplateService>.Instance);
        return new Fixture(module, store, service);
    }

    private sealed record Fixture(TestGameModule Module, InMemoryGamePromptTemplateStore Store, GamePromptTemplateService Service);

    private sealed class InMemoryGamePromptTemplateStore : IGamePromptTemplateStore
    {
        private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListAsync(string moduleId, CancellationToken ct = default)
        {
            var prefix = moduleId + "/";
            var items = _documents
                .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(item => new GameUserPromptTemplateInfo
                {
                    ModuleId = moduleId,
                    Name = item.Key[prefix.Length..],
                    FileName = item.Key[prefix.Length..] + ".md",
                    RelativePath = "game-prompts/" + item.Key + ".md",
                    Tokens = item.Value.Length / 4,
                    Size = item.Value.Length,
                })
                .ToArray();
            return Task.FromResult<IReadOnlyList<GameUserPromptTemplateInfo>>(items);
        }

        public Task<GameUserPromptTemplateDocument?> TryLoadAsync(string moduleId, string promptName, CancellationToken ct = default)
        {
            return Task.FromResult(_documents.TryGetValue(Key(moduleId, promptName), out var content)
                ? new GameUserPromptTemplateDocument
                {
                    ModuleId = moduleId,
                    Name = promptName,
                    FileName = promptName + ".md",
                    RelativePath = $"game-prompts/{moduleId}/{promptName}.md",
                    Content = content,
                }
                : null);
        }

        public Task SaveAsync(string moduleId, string promptName, string content, CancellationToken ct = default)
        {
            _documents[Key(moduleId, promptName)] = content;
            return Task.CompletedTask;
        }

        public Task<GameUserPromptTemplateDocument> CreateUniqueAsync(string moduleId, string basePromptName, string content, CancellationToken ct = default)
        {
            var name = basePromptName;
            var index = 2;
            while (_documents.ContainsKey(Key(moduleId, name)))
            {
                name = $"{basePromptName}-{index++}";
            }

            _documents[Key(moduleId, name)] = content;
            return TryLoadAsync(moduleId, name, ct).ContinueWith(task => task.Result!, ct);
        }

        private static string Key(string moduleId, string promptName) => moduleId + "/" + promptName;
    }

    private sealed class TestGameModule : IGameModule
    {
        public const string ModuleId = "prompt-test";

        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId(ModuleId),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Prompt Test",
            new PlayerCountRange(1, 4),
            []);

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context) =>
            RulesGameState.CreateNotStarted(context.GameInstanceId, Descriptor, context.Seed, []);

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() =>
        [
            new GamePromptAsset("rules", GamePromptAssetKind.RulesText, "Rules text."),
            new GamePromptAsset("participant", GamePromptAssetKind.ParticipantInstructions, "Bundled participant instructions."),
        ];
    }
}
