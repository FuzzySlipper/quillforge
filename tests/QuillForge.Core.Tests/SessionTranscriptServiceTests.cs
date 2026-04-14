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
