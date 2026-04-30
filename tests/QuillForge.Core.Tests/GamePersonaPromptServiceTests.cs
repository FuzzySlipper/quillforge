using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GamePersonaPromptServiceTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsNoneWhenSelectionIsNone()
    {
        var service = new GamePersonaPromptService(new InMemoryGamePersonaPromptStore(), NullLogger<GamePersonaPromptService>.Instance);

        var resolved = await service.ResolveAsync(GamePersonaPromptSelection.None);

        Assert.Null(resolved.Content);
        Assert.Equal(GamePersonaPromptSource.None, resolved.Selection.Source);
        Assert.False(resolved.UsedFallback);
    }

    [Fact]
    public async Task ResolveAsync_LoadsSelectedUserPersonaPrompt()
    {
        var store = new InMemoryGamePersonaPromptStore();
        store.Documents["wolf"] = "Play a confident werewolf.";
        var service = new GamePersonaPromptService(store, NullLogger<GamePersonaPromptService>.Instance);

        var resolved = await service.ResolveAsync(GamePersonaPromptSelection.ForUserPrompt("wolf"));

        Assert.Equal("Play a confident werewolf.", resolved.Content);
        Assert.Equal(GamePersonaPromptSource.User, resolved.Selection.Source);
        Assert.False(resolved.UsedFallback);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToNoneWhenSelectedPersonaIsMissingOrBlank()
    {
        var store = new InMemoryGamePersonaPromptStore();
        store.Documents["blank"] = "   ";
        var service = new GamePersonaPromptService(store, NullLogger<GamePersonaPromptService>.Instance);

        var missing = await service.ResolveAsync(GamePersonaPromptSelection.ForUserPrompt("missing"));
        var blank = await service.ResolveAsync(GamePersonaPromptSelection.ForUserPrompt("blank"));

        Assert.Null(missing.Content);
        Assert.Equal(GamePersonaPromptSource.None, missing.Selection.Source);
        Assert.True(missing.UsedFallback);
        Assert.Equal("persona_prompt_missing", missing.FallbackReason);
        Assert.Null(blank.Content);
        Assert.Equal("persona_prompt_blank", blank.FallbackReason);
    }

    [Fact]
    public async Task CreateForEditAsync_UsesDefaultSeedWhenNoLegacyContentExists()
    {
        var store = new InMemoryGamePersonaPromptStore();
        var service = new GamePersonaPromptService(store, NullLogger<GamePersonaPromptService>.Instance);

        var document = await service.CreateForEditAsync("new agent", seedContent: "   ");

        Assert.Equal("new agent", document.Name);
        Assert.Contains("Describe the reusable persona", store.Documents[document.Name], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateForEditAsync_SeedsPromptAndUsesCollisionSafeName()
    {
        var store = new InMemoryGamePersonaPromptStore();
        var service = new GamePersonaPromptService(store, NullLogger<GamePersonaPromptService>.Instance);

        var first = await service.CreateForEditAsync("agent persona", "Legacy text");
        var second = await service.CreateForEditAsync("agent persona", "Other text");

        Assert.Equal("agent persona", first.Name);
        Assert.Equal("agent persona-2", second.Name);
        Assert.Equal("Legacy text", store.Documents[first.Name]);
        Assert.Equal("Other text", store.Documents[second.Name]);
    }

    private sealed class InMemoryGamePersonaPromptStore : IGamePersonaPromptStore
    {
        public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameUserPersonaPromptInfo>>(Documents.Keys
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(name => new GameUserPersonaPromptInfo
                {
                    Name = name,
                    FileName = name + ".md",
                    RelativePath = "game-personas/" + name + ".md",
                    Tokens = Documents[name].Length / 4,
                    Size = Documents[name].Length,
                })
                .ToArray());

        public Task<GameUserPersonaPromptDocument?> TryLoadAsync(string promptName, CancellationToken ct = default) =>
            Task.FromResult(Documents.TryGetValue(promptName, out var content)
                ? new GameUserPersonaPromptDocument
                {
                    Name = promptName,
                    FileName = promptName + ".md",
                    RelativePath = "game-personas/" + promptName + ".md",
                    Content = content,
                }
                : null);

        public Task SaveAsync(string promptName, string content, CancellationToken ct = default)
        {
            Documents[promptName] = content;
            return Task.CompletedTask;
        }

        public Task<GameUserPersonaPromptDocument> CreateUniqueAsync(string basePromptName, string content, CancellationToken ct = default)
        {
            for (var index = 0; index < 100; index++)
            {
                var candidate = index == 0 ? basePromptName : $"{basePromptName}-{index + 1}";
                if (Documents.ContainsKey(candidate)) continue;
                Documents[candidate] = content;
                return Task.FromResult(new GameUserPersonaPromptDocument
                {
                    Name = candidate,
                    FileName = candidate + ".md",
                    RelativePath = "game-personas/" + candidate + ".md",
                    Content = content,
                });
            }

            throw new InvalidOperationException();
        }
    }
}
