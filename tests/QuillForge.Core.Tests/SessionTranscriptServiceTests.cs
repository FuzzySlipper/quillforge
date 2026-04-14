using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class SessionTranscriptServiceTests
{
    [Fact]
    public async Task SyncRoleplayTranscriptAsync_WritesOnlyRoleplayAssistantTurnsFromActiveThread()
    {
        var sessionId = Guid.CreateVersion7();
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(sessionStore, runtimeStore, storyStore);

        var tree = new ConversationTree(sessionId, "Roleplay Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.ActiveLeafId, "user", new MessageContent("Open the gate."));
        tree.Append(
            tree.ActiveLeafId,
            "assistant",
            new MessageContent("The iron gate groans open into the rain."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        tree.Append(tree.ActiveLeafId, "user", new MessageContent("What do I see?"));
        tree.Append(
            tree.ActiveLeafId,
            "assistant",
            new MessageContent("Out of character planning note."),
            new MessageMetadata
            {
                ConversationMode = Mode.Guide,
            });
        tree.Append(tree.ActiveLeafId, "user", new MessageContent("Keep going."));
        tree.Append(
            tree.ActiveLeafId,
            "assistant",
            new MessageContent("  Lanternlight catches on wet stone.  "),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await sessionStore.SaveAsync(tree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "campaign-alpha",
                CurrentFile = "scene-07.md",
            },
        });

        await service.SyncRoleplayTranscriptAsync(sessionId);

        var savedTranscript = await storyStore.ReadAsync("campaign-alpha", "scene-07.md");
        Assert.Equal(
            "The iron gate groans open into the rain.\n\nLanternlight catches on wet stone.",
            savedTranscript);
    }

    [Fact]
    public async Task SyncRoleplayTranscriptAsync_OutsideRoleplayMode_DoesNotWriteTranscript()
    {
        var sessionId = Guid.CreateVersion7();
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(sessionStore, runtimeStore, storyStore);

        var tree = new ConversationTree(sessionId, "Guide Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.ActiveLeafId, "user", new MessageContent("What does the map mean?"));
        tree.Append(
            tree.ActiveLeafId,
            "assistant",
            new MessageContent("The map marks the old harbor."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await sessionStore.SaveAsync(tree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Guide,
                ProjectName = "campaign-alpha",
                CurrentFile = "scene-07.md",
            },
        });

        await service.SyncRoleplayTranscriptAsync(sessionId);

        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("campaign-alpha", "scene-07.md"));
    }

    [Fact]
    public async Task SyncRoleplayTranscriptAsync_AfterDelete_RewritesArtifactToActiveThread()
    {
        var sessionId = Guid.CreateVersion7();
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(sessionStore, runtimeStore, storyStore);

        var tree = new ConversationTree(sessionId, "Roleplay Session", NullLogger<ConversationTree>.Instance);
        var user1 = tree.Append(tree.RootId, "user", new MessageContent("Open the trapdoor."));
        var assistant1 = tree.Append(
            user1.Id,
            "assistant",
            new MessageContent("Rowan eases the trapdoor open and listens for the tide below."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        var user2 = tree.Append(assistant1.Id, "user", new MessageContent("Ask him about Captain Rowe."));
        var assistant2 = tree.Append(
            user2.Id,
            "assistant",
            new MessageContent("At Captain Rowe's name, Rowan's jaw tightens."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await sessionStore.SaveAsync(tree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "campaign-alpha",
                CurrentFile = "scene-07.md",
            },
        });

        await service.SyncRoleplayTranscriptAsync(sessionId);
        tree.Delete(assistant2.Id);
        await sessionStore.SaveAsync(tree);
        await service.SyncRoleplayTranscriptAsync(sessionId);

        var savedTranscript = await storyStore.ReadAsync("campaign-alpha", "scene-07.md");
        Assert.Equal(
            "Rowan eases the trapdoor open and listens for the tide below.",
            savedTranscript);
    }

    [Fact]
    public async Task SyncRoleplayTranscriptAsync_AfterRegenerate_WritesActiveVariantOnly()
    {
        var sessionId = Guid.CreateVersion7();
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(sessionStore, runtimeStore, storyStore);

        var tree = new ConversationTree(sessionId, "Roleplay Session", NullLogger<ConversationTree>.Instance);
        var user1 = tree.Append(tree.RootId, "user", new MessageContent("Lead me to the tide tunnels."));
        var assistant1 = tree.Append(
            user1.Id,
            "assistant",
            new MessageContent("Rowan leads you down the wet stone steps."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        var user2 = tree.Append(assistant1.Id, "user", new MessageContent("What does he say about the contraband?"));
        var originalReply = tree.Append(
            user2.Id,
            "assistant",
            new MessageContent("He denies knowing anything about it."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await sessionStore.SaveAsync(tree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "campaign-alpha",
                CurrentFile = "scene-07.md",
            },
        });

        await service.SyncRoleplayTranscriptAsync(sessionId);
        tree.CreateVariant(
            originalReply.Id,
            new MessageContent("He admits Captain Rowe already suspects the tide tunnels."),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        await sessionStore.SaveAsync(tree);
        await service.SyncRoleplayTranscriptAsync(sessionId);

        var savedTranscript = await storyStore.ReadAsync("campaign-alpha", "scene-07.md");
        Assert.Equal(
            "Rowan leads you down the wet stone steps.\n\nHe admits Captain Rowe already suspects the tide tunnels.",
            savedTranscript);
    }

    private static SessionTranscriptService CreateService(
        InMemorySessionStore sessionStore,
        InMemorySessionRuntimeStore runtimeStore,
        InMemoryStoryStore storyStore)
    {
        return new SessionTranscriptService(
            sessionStore,
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            storyStore,
            NullLogger<SessionTranscriptService>.Instance);
    }
}
