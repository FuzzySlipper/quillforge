using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class NarrativeDirectorAgentTests
{
    [Fact]
    public async Task DirectSceneAsync_BuildsPromptWithNarrativeRulesAndSessionContext()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("The captain steps aside and lets her enter.");

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, new EmptyLoreStore(), new FakeContentFileService(), NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var result = await agent.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = "I ask the captain to open the gate.",
            },
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = "roleplay",
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                LastAssistantResponse = "The captain narrows his eyes and keeps the gate shut.",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveModeName = "roleplay",
                    ProjectName = "gatehouse",
                    CurrentFile = "chapter-01.md",
                    CharacterSection = "Captain Elian guards the city gate.",
                    StoryStateSummary = "The gate is closed due to curfew.",
                    StoryStatePath = "gatehouse/.state.yaml",
                    DirectorNotes = "The captain is suspicious but not hostile.",
                    ActivePlotFile = "gate-arc",
                    ActivePlotContent = "# Gate Arc\n\n- Beat: let the guard test her resolve.",
                    PlotProgressSummary = "Current beat: gate-confrontation",
                },
            });

        Assert.Equal("The captain steps aside and lets her enter.", result.ResponseText);

        var request = fake.ReceivedRequests.Single();
        Assert.Contains("Narrative Director", request.SystemPrompt!);
        Assert.Contains("Let user actions matter", request.SystemPrompt!);
        Assert.Contains("curfew", request.SystemPrompt!);
        Assert.Contains("Captain Elian", request.SystemPrompt!);
        Assert.Contains("suspicious but not hostile", request.SystemPrompt!);
        Assert.Contains("Gate Arc", request.SystemPrompt!);
        Assert.Contains("Current beat: gate-confrontation", request.SystemPrompt!);
        Assert.Contains("keeps the gate shut", request.Messages.Single().Content.GetText());
        Assert.Contains("write_prose", request.Tools!.Select(t => t.Name));
        Assert.Contains("update_narrative_state", request.Tools!.Select(t => t.Name));
    }

    [Fact]
    public async Task GeneratePlotAsync_BuildsReusablePlotMarkdown()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("# Moonfall Arc\n\n## Premise\nThe court turns on itself.");

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, new EmptyLoreStore(), new FakeContentFileService(), NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var result = await agent.GeneratePlotAsync(
            new PlotGenerationRequest { Prompt = "court intrigue tragedy" },
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = "roleplay",
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveModeName = "roleplay",
                    ProjectName = "moonfall",
                    StoryStatePath = "moonfall/.state.yaml",
                    CharacterSection = "Princess Ilya is brilliant and reckless.",
                },
            });

        Assert.Contains("Moonfall Arc", result.Markdown);
        var request = fake.ReceivedRequests.Single();
        Assert.Contains("reusable plot arc document", request.SystemPrompt!);
        Assert.Contains("Princess Ilya", request.SystemPrompt!);
        Assert.Contains("court intrigue tragedy", request.Messages.Single().Content.GetText());
        Assert.Contains("query_lore", request.Tools!.Select(t => t.Name));
    }
}

internal sealed class FakeNarrativeRulesStore : INarrativeRulesStore
{
    public Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
    {
        return Task.FromResult("Keep tension rising. Let user actions matter.");
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}

internal sealed class EmptyLoreStore : ILoreStore
{
    public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(string loreSetName, string query, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<(string FilePath, string Snippet)>>([]);
    }
}

internal sealed class FakeSessionRuntimeService : ISessionStateService
{
    public Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        return Task.FromResult(new SessionState { SessionId = sessionId });
    }

    public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
    {
        return Task.FromResult(SessionMutationResult<SessionState>.Success(
            new SessionState
            {
                SessionId = sessionId,
                Narrative = new NarrativeRuntimeState
                {
                    DirectorNotes = command.DirectorNotes,
                    ActivePlotFile = command.ActivePlotFile,
                    PlotProgress = new PlotProgressState
                    {
                        CurrentBeat = command.PlotProgress?.CurrentBeat,
                        CompletedBeats = command.PlotProgress?.CompletedBeats?.ToList() ?? [],
                        Deviations = command.PlotProgress?.Deviations?.ToList() ?? [],
                    },
                },
            }));
    }
}
