using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemGameTemplateStoreTests
{
    [Fact]
    public async Task SaveLoadListAndDelete_RoundTripJsonTemplateUnderContentPath()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);
            var template = CreateTemplate();

            await store.SaveAsync("village", template);
            var listed = await store.ListAsync();
            var loaded = await store.LoadAsync("village");
            await store.DeleteAsync("village");

            Assert.Equal(["village"], listed);
            Assert.Equal("village", loaded.TemplateId);
            Assert.Equal("werewolf", loaded.Module.ModuleId);
            Assert.Equal(1, loaded.RulesOptions.Values.Single(value => value.Name == "werewolf_count").IntValue);
            Assert.False(await store.ExistsAsync("village"));
            Assert.True(Directory.Exists(Path.Combine(root, ContentPaths.GameTemplates)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsPathTraversalTemplateIds()
    {
        var root = CreateTempRoot();
        try
        {
            var store = CreateStore(root);

            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("../escape", CreateTemplate()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileSystemGameTemplateStore CreateStore(string root) =>
        new(
            root,
            new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
            NullLogger<FileSystemGameTemplateStore>.Instance);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quillforge-game-templates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static GameTemplate CreateTemplate() =>
        new()
        {
            TemplateId = "village",
            DisplayName = "Village",
            Module = new GameTemplateModuleSelection
            {
                ModuleId = "werewolf",
                MinimumVersion = "1.0.0",
                MaximumVersion = "1.0.0",
            },
            RulesOptions = new GameTemplateRulesOptions
            {
                Values =
                [
                    new GameTemplateRuleOptionValue { Name = "werewolf_count", Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                    new GameTemplateRuleOptionValue { Name = "seer_enabled", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                ],
            },
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 4,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-1",
                        ProviderAlias = "local",
                        FixedName = "Bob",
                    },
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-2",
                        ProviderAlias = "local",
                        FixedName = "Carol",
                    },
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-3",
                        ProviderAlias = "local",
                        FixedName = "Drew",
                    }
                ],
            },
            Memory = new GameTemplateMemorySettings { TokenBudget = 512 },
        };
}
