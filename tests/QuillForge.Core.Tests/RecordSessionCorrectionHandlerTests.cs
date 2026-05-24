using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class RecordSessionCorrectionHandlerTests
{
    [Fact]
    public async Task HandleAsync_AppendsCorrectionToStickySessionCanon()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Keep tension rising.",
                StickySessionCanon = "- Rowan carries the lighthouse keeper's ring.",
            },
        });

        var runtimeService = new SessionRuntimeService(
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            new FakeProfileConfigService(),
            new InMemoryStoryStore(),
            [],
            NullLogger<SessionRuntimeService>.Instance);

        var handler = new RecordSessionCorrectionHandler(
            runtimeService,
            NullLogger<RecordSessionCorrectionHandler>.Instance);

        var result = await handler.HandleAsync(
            new ToolInput(System.Text.Json.JsonDocument.Parse("""{"correction_text":"Zayne knew where she lived because he tutored her at Gran's house","fact_category":"location"}""").RootElement),
            new AgentContext { SessionId = sessionId, ActiveMode = Mode.Roleplay });

        Assert.True(result.Success);
        Assert.Contains("Correction recorded in sticky session canon", result.Content);

        var updatedState = await runtimeStore.LoadAsync(sessionId);
        Assert.Contains("Zayne knew where she lived because he tutored her at Gran's house", updatedState.Narrative.StickySessionCanon);
        Assert.Contains("[location correction]", updatedState.Narrative.StickySessionCanon);
        Assert.Contains("Rowan carries the lighthouse keeper's ring", updatedState.Narrative.StickySessionCanon);
    }

    [Fact]
    public async Task HandleAsync_CreatesStickySessionCanon_WhenNoneExists()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
        });

        var runtimeService = new SessionRuntimeService(
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            new FakeProfileConfigService(),
            new InMemoryStoryStore(),
            [],
            NullLogger<SessionRuntimeService>.Instance);

        var handler = new RecordSessionCorrectionHandler(
            runtimeService,
            NullLogger<RecordSessionCorrectionHandler>.Instance);

        var result = await handler.HandleAsync(
            new ToolInput(System.Text.Json.JsonDocument.Parse("""{"correction_text":"Caleb is April's older brother, not her cousin."}""").RootElement),
            new AgentContext { SessionId = sessionId, ActiveMode = Mode.Roleplay });

        Assert.True(result.Success);

        var updatedState = await runtimeStore.LoadAsync(sessionId);
        Assert.Contains("[Correction] Caleb is April's older brother, not her cousin.", updatedState.Narrative.StickySessionCanon);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenCorrectionTextIsEmpty()
    {
        var handler = new RecordSessionCorrectionHandler(
            new FakeSessionRuntimeService(),
            NullLogger<RecordSessionCorrectionHandler>.Instance);

        var result = await handler.HandleAsync(
            new ToolInput(System.Text.Json.JsonDocument.Parse("""{"correction_text":"   "}""").RootElement),
            new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Roleplay });

        Assert.False(result.Success);
        Assert.Contains("correction_text is required", result.Error);
    }

    [Fact]
    public async Task HandleAsync_UpdatesDirectorNotesWithCorrectionSummary()
    {
        var sessionId = Guid.CreateVersion7();
        var runtimeStore = new InMemorySessionRuntimeStore();
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "The captain is suspicious.",
            },
        });

        var runtimeService = new SessionRuntimeService(
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            new FakeProfileConfigService(),
            new InMemoryStoryStore(),
            [],
            NullLogger<SessionRuntimeService>.Instance);

        var handler = new RecordSessionCorrectionHandler(
            runtimeService,
            NullLogger<RecordSessionCorrectionHandler>.Instance);

        await handler.HandleAsync(
            new ToolInput(System.Text.Json.JsonDocument.Parse("""{"correction_text":"The flowers were lilies, not roses."}""").RootElement),
            new AgentContext { SessionId = sessionId, ActiveMode = Mode.Roleplay });

        var updatedState = await runtimeStore.LoadAsync(sessionId);
        Assert.Contains("User correction recorded: The flowers were lilies, not roses.", updatedState.Narrative.DirectorNotes);
        Assert.Contains("The captain is suspicious.", updatedState.Narrative.DirectorNotes);
    }
}
