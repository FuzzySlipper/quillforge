using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class SessionLoreCanonizationServiceTests
{
    private static readonly IMode[] Modes =
    [
        new GuideMode(),
        new WriterMode(),
        new RoleplayMode(),
        new ForgeMode(),
        new CouncilMode(),
    ];

    [Fact]
    public async Task GenerateProposalAndApplyAsync_PersistsPendingProposal_AndWritesLoreFile()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var sessionStore = new InMemorySessionStore();
        var completion = new FakeCompletionService();
        var contentFiles = new FakeContentFileService();
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["history.md"] = "# History\n\nThe citadel was founded centuries ago.",
            ["characters/warden.md"] = "# Warden\n\nThe warden serves the citadel.",
        });
        var runtimeService = CreateRuntimeService(runtimeStore);
        var service = CreateService(sessionStore, runtimeStore, runtimeService, loreStore, contentFiles, completion);

        var tree = new ConversationTree(sessionId, "Canon Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.RootId, "user", new MessageContent("Let's lock in canon."));
        tree.Append(tree.ActiveLeafId, "assistant", new MessageContent("The citadel's silver bells cracked during the ash storm, and Warden Ilya now stores the fragments in the archive vault."));
        await sessionStore.SaveAsync(tree);
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Profile = new ProfileState { ProfileId = "grim" },
        });

        completion.EnqueueText(
            """
            {
              "summary": "Found one new artifact detail and one update to the citadel record.",
              "new_facts": ["Warden Ilya stores the cracked silver bell fragments in the archive vault."],
              "modified_facts": ["The citadel's silver bells cracked during the ash storm."],
              "conflicts": [],
              "proposed_markdown": "### Ash Storm Aftermath\n\n- The citadel's silver bells cracked during the ash storm.\n- Warden Ilya stores the cracked silver bell fragments in the archive vault."
            }
            """);

        var preview = await service.GenerateProposalAsync(sessionId, new GenerateLoreCanonizationProposalCommand("history.md"));

        Assert.Equal(SessionMutationStatus.Success, preview.Status);
        Assert.NotNull(preview.Value);
        Assert.True(preview.Value.Proposal.CanApply);
        Assert.Equal("grim-lore", preview.Value.Proposal.LoreSet);
        Assert.Equal("history.md", preview.Value.Proposal.TargetFilePath);
        Assert.Single(preview.Value.Proposal.NewFacts);
        Assert.Single(preview.Value.Proposal.ModifiedFacts);
        Assert.Contains("Ash Storm Aftermath", preview.Value.Proposal.ProposedMarkdown);

        var persistedState = await runtimeStore.LoadAsync(sessionId);
        Assert.NotNull(persistedState.Canonization?.PendingProposal);
        Assert.Equal("history.md", persistedState.Canonization?.PendingProposal?.TargetFilePath);

        var apply = await service.ApplyProposalAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, apply.Status);
        Assert.NotNull(apply.Value);
        Assert.Equal("history.md", apply.Value.TargetFilePath);
        Assert.True(contentFiles.Files.TryGetValue("lore/grim-lore/history.md", out var writtenContent));
        Assert.Contains("quillforge:canonize", writtenContent);
        Assert.Contains("Ash Storm Aftermath", writtenContent);
        Assert.Contains("Source session:", writtenContent);

        var clearedState = await runtimeStore.LoadAsync(sessionId);
        Assert.Null(clearedState.Canonization?.PendingProposal);
        Assert.Single(completion.ReceivedRequests);
        Assert.Equal("default", completion.ReceivedRequests[0].Model);
    }

    [Fact]
    public async Task ConflictOnlyProposal_CannotBeApplied()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var sessionStore = new InMemorySessionStore();
        var completion = new FakeCompletionService();
        var contentFiles = new FakeContentFileService();
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["history.md"] = "# History\n\nThe silver bells were melted down after the storm.",
        });
        var runtimeService = CreateRuntimeService(runtimeStore);
        var service = CreateService(sessionStore, runtimeStore, runtimeService, loreStore, contentFiles, completion);

        var tree = new ConversationTree(sessionId, "Conflict Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.RootId, "assistant", new MessageContent("The bells were never damaged at all."));
        await sessionStore.SaveAsync(tree);
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Profile = new ProfileState { ProfileId = "grim" },
            Canonization = new LoreCanonizationRuntimeState(),
        });

        completion.EnqueueText(
            """
            {
              "summary": "The session conflicts with established lore about the bells.",
              "new_facts": [],
              "modified_facts": [],
              "conflicts": ["The session says the bells were undamaged, but existing lore says they were melted down after the storm."],
              "proposed_markdown": ""
            }
            """);

        var preview = await service.GenerateProposalAsync(sessionId, new GenerateLoreCanonizationProposalCommand("history.md"));
        var apply = await service.ApplyProposalAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, preview.Status);
        Assert.NotNull(preview.Value);
        Assert.False(preview.Value.Proposal.CanApply);
        Assert.Single(preview.Value.Proposal.Conflicts);
        Assert.Equal(SessionMutationStatus.Invalid, apply.Status);
        Assert.Empty(contentFiles.Files);
    }

    [Fact]
    public async Task DiscardProposalAsync_ClearsPendingProposal()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        var sessionStore = new InMemorySessionStore();
        var completion = new FakeCompletionService();
        var contentFiles = new FakeContentFileService();
        var loreStore = new ConfigurableLoreStore();
        var runtimeService = CreateRuntimeService(runtimeStore);
        var service = CreateService(sessionStore, runtimeStore, runtimeService, loreStore, contentFiles, completion);

        var tree = new ConversationTree(sessionId, "Discard Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.RootId, "assistant", new MessageContent("Archivist Sen keeps the ember ledger."));
        await sessionStore.SaveAsync(tree);
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Profile = new ProfileState { ProfileId = "grim" },
        });

        completion.EnqueueText(
            """
            {
              "summary": "Found one new keeper detail.",
              "new_facts": ["Archivist Sen keeps the ember ledger."],
              "modified_facts": [],
              "conflicts": [],
              "proposed_markdown": "### Archivist Sen\n\n- Archivist Sen keeps the ember ledger."
            }
            """);

        var preview = await service.GenerateProposalAsync(sessionId, new GenerateLoreCanonizationProposalCommand("characters/sen.md"));
        var discard = await service.DiscardProposalAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, preview.Status);
        Assert.Equal(SessionMutationStatus.Success, discard.Status);
        Assert.Equal("characters/sen.md", discard.Value?.TargetFilePath);
        var persisted = await runtimeStore.LoadAsync(sessionId);
        Assert.Null(persisted.Canonization?.PendingProposal);
    }

    private static SessionRuntimeService CreateRuntimeService(InMemorySessionRuntimeStore runtimeStore)
    {
        return new SessionRuntimeService(
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            new FakeProfileConfigService(),
            new InMemoryStoryStore(),
            Modes,
            NullLogger<SessionRuntimeService>.Instance);
    }

    private static SessionLoreCanonizationService CreateService(
        InMemorySessionStore sessionStore,
        InMemorySessionRuntimeStore runtimeStore,
        SessionRuntimeService runtimeService,
        ILoreStore loreStore,
        FakeContentFileService contentFiles,
        FakeCompletionService completion)
    {
        return new SessionLoreCanonizationService(
            sessionStore,
            runtimeStore,
            runtimeService,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            loreStore,
            contentFiles,
            completion,
            new AppConfig(),
            NullLogger<SessionLoreCanonizationService>.Instance);
    }
}
