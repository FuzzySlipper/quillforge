using System.Reflection;
using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Architecture.Tests;

/// <summary>
/// Architecture tests for the per-session state hierarchy (Task 43).
/// Verifies type definitions, ownership boundaries, and persistence interfaces.
/// </summary>
public class SessionStateTests
{
    [Fact]
    public void SessionState_OwnsExactlySixSubStates()
    {
        // The aggregate should have exactly Mode, Profile, Roleplay, Writer,
        // Narrative, and Canonization sub-states
        // plus SessionId and LastModified metadata. No flat bag of nullable fields.
        var props = typeof(SessionState).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var subStateProps = props.Where(p =>
            p.PropertyType == typeof(ModeSelectionState) ||
            p.PropertyType == typeof(ProfileState) ||
            p.PropertyType == typeof(RoleplayRuntimeState) ||
            p.PropertyType == typeof(WriterRuntimeState) ||
            p.PropertyType == typeof(NarrativeRuntimeState) ||
            p.PropertyType == typeof(LoreCanonizationRuntimeState))
            .ToList();

        Assert.Equal(6, subStateProps.Count);
        Assert.Contains(subStateProps, p => p.Name == "Mode");
        Assert.Contains(subStateProps, p => p.Name == "Profile");
        Assert.Contains(subStateProps, p => p.Name == "Roleplay");
        Assert.Contains(subStateProps, p => p.Name == "Writer");
        Assert.Contains(subStateProps, p => p.Name == "Narrative");
        Assert.Contains(subStateProps, p => p.Name == "Canonization");
    }

    [Fact]
    public void SubStates_AreNotNullByDefault()
    {
        var state = new SessionState();
        Assert.NotNull(state.Mode);
        Assert.NotNull(state.Profile);
        Assert.NotNull(state.Roleplay);
        Assert.NotNull(state.Writer);
        Assert.NotNull(state.Narrative);
        Assert.Null(state.Canonization);
    }

    [Fact]
    public void ModeSelectionState_DefaultsToGuide()
    {
        var mode = new ModeSelectionState();
        Assert.Equal(Mode.Guide, mode.ActiveMode);
        Assert.Null(mode.ProjectName);
        Assert.Null(mode.CurrentFile);
        Assert.Null(mode.Character);
    }

    [Fact]
    public void ProfileState_DefaultsToNull_MeaningUseDefaultProfileWithNoOverrides()
    {
        var profile = new ProfileState();
        Assert.Null(profile.ProfileId);
        Assert.Null(profile.ActiveLoreSet);
        Assert.Null(profile.ActiveWritingStyle);
    }

    [Fact]
    public void ProfileState_IgnoresLegacyConductorFieldsDuringDeserialization()
    {
        var state = JsonSerializer.Deserialize<SessionState>(
            """
            {
              "profile": {
                "profileId": "grim",
                "activeConductor": "legacy-conductor",
                "activePersona": "legacy-conductor"
              }
            }
            """,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

        Assert.NotNull(state);
        Assert.Equal("grim", state.Profile.ProfileId);
        Assert.Null(state.Profile.IgnoredLegacyActiveConductor);
        Assert.Null(state.Profile.IgnoredLegacyActivePersona);
    }

    [Fact]
    public void LoreCanonizationState_DefaultsToNull()
    {
        var state = new SessionState();
        Assert.Null(state.Canonization);
    }

    [Fact]
    public void WriterRuntimeState_DefaultsToIdle()
    {
        var writer = new WriterRuntimeState();
        Assert.Equal(WriterState.Idle, writer.State);
        Assert.Null(writer.PendingContent);
        Assert.Null(writer.PendingProjectName);
        Assert.Null(writer.PendingFileName);
    }

    [Fact]
    public void RoleplayRuntimeState_DefaultsToProfileDrivenSelections()
    {
        var roleplay = new RoleplayRuntimeState();
        Assert.False(roleplay.HasExplicitAiCharacterSelection);
        Assert.Null(roleplay.ActiveAiCharacter);
        Assert.False(roleplay.HasExplicitUserCharacterSelection);
        Assert.Null(roleplay.ActiveUserCharacter);
    }

    [Fact]
    public void ModeFields_DoNotExist_OnTopLevelAggregate()
    {
        // Ensure mode-specific fields aren't duplicated at the top level
        var topProps = typeof(SessionState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.DoesNotContain("ActiveModeName", topProps);
        Assert.DoesNotContain("ProjectName", topProps);
        Assert.DoesNotContain("CurrentFile", topProps);
        Assert.DoesNotContain("Character", topProps);
        Assert.DoesNotContain("WriterPendingContent", topProps);
    }

    [Fact]
    public void ISessionStateStore_ExistsInCore()
    {
        var storeType = typeof(ISessionStateStore);
        Assert.True(storeType.IsInterface);
        Assert.Equal("QuillForge.Core", storeType.Assembly.GetName().Name);
    }

    [Fact]
    public void ISessionStateStore_HasLoadSaveDelete()
    {
        var methods = typeof(ISessionStateStore).GetMethods()
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Contains("LoadAsync", methods);
        Assert.Contains("SaveAsync", methods);
        Assert.Contains("DeleteAsync", methods);
    }

    [Fact]
    public void ISessionStateService_ExistsInCore()
    {
        var serviceType = typeof(ISessionStateService);
        Assert.True(serviceType.IsInterface);
        Assert.Equal("QuillForge.Core", serviceType.Assembly.GetName().Name);
    }

    [Fact]
    public void ISessionMutationGate_ExistsInCore()
    {
        var gateType = typeof(ISessionMutationGate);
        Assert.True(gateType.IsInterface);
        Assert.Equal("QuillForge.Core", gateType.Assembly.GetName().Name);
    }

    [Fact]
    public void ISessionLifecycleService_ExistsInCore()
    {
        var serviceType = typeof(ISessionLifecycleService);
        Assert.True(serviceType.IsInterface);
        Assert.Equal("QuillForge.Core", serviceType.Assembly.GetName().Name);
    }

    [Fact]
    public void IInteractiveSessionContextService_ExistsInCore()
    {
        var serviceType = typeof(IInteractiveSessionContextService);
        Assert.True(serviceType.IsInterface);
        Assert.Equal("QuillForge.Core", serviceType.Assembly.GetName().Name);
    }

    [Fact]
    public void OrchestratorAgent_DoesNotOwnModeMutation()
    {
        var methods = typeof(OrchestratorAgent).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.DoesNotContain("SetMode", methods);
    }

    [Fact]
    public void OrchestratorAgent_DoesNotHydrateSessionContextInternally()
    {
        var methods = typeof(OrchestratorAgent).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.DoesNotContain("HydrateModeContextAsync", methods);
    }

    [Fact]
    public void WriterMode_DoesNotOwnWriterStateMutationHelpers()
    {
        var methods = typeof(WriterMode).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.DoesNotContain("CaptureIfPending", methods);
        Assert.DoesNotContain("Accept", methods);
        Assert.DoesNotContain("Reject", methods);
        Assert.DoesNotContain("Reset", methods);
    }

    [Fact]
    public void SessionState_FullyPopulated_HasExpectedValues()
    {
        var sessionId = Guid.NewGuid();
        var state = new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "my-novel",
                CurrentFile = "chapter1.md",
                Character = "hero",
            },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveLoreSet = "fantasy",
                ActiveWritingStyle = "literary",
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "guide",
                HasExplicitUserCharacterSelection = true,
                ActiveUserCharacter = "author",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "pending text",
                PendingProjectName = "my-novel",
                PendingFileName = "chapter1.md",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "track the rising pressure",
                ActivePlotFile = "arc-one",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "midpoint",
                },
            },
            Canonization = new LoreCanonizationRuntimeState
            {
                PendingProposal = new LoreCanonizationProposalState
                {
                    SessionId = sessionId,
                    LoreSet = "fantasy",
                    TargetFilePath = "history.md",
                    Summary = "Captured one pending lore import.",
                    NewFacts = ["The bells cracked during the storm."],
                    ModifiedFacts = [],
                    Conflicts = [],
                    ProposedMarkdown = """
                        ### Storm

                        - The bells cracked during the storm.
                        """,
                    ProposedFileContent = """
                        ### Storm

                        - The bells cracked during the storm.
                        """,
                    CanApply = true,
                },
            },
        };

        Assert.Equal(sessionId, state.SessionId);
        Assert.Equal(Mode.Writer, state.Mode.ActiveMode);
        Assert.Equal("my-novel", state.Mode.ProjectName);
        Assert.Equal("chapter1.md", state.Mode.CurrentFile);
        Assert.Equal("hero", state.Mode.Character);
        Assert.Equal("grim", state.Profile.ProfileId);
        Assert.Equal("fantasy", state.Profile.ActiveLoreSet);
        Assert.Equal("literary", state.Profile.ActiveWritingStyle);
        Assert.Equal("guide", state.Roleplay.ActiveAiCharacter);
        Assert.Equal("author", state.Roleplay.ActiveUserCharacter);
        Assert.Equal("pending text", state.Writer.PendingContent);
        Assert.Equal("my-novel", state.Writer.PendingProjectName);
        Assert.Equal("chapter1.md", state.Writer.PendingFileName);
        Assert.Equal(WriterState.PendingReview, state.Writer.State);
        Assert.Equal("track the rising pressure", state.Narrative.DirectorNotes);
        Assert.Equal("arc-one", state.Narrative.ActivePlotFile);
        Assert.Equal("midpoint", state.Narrative.PlotProgress.CurrentBeat);
        Assert.NotNull(state.Canonization?.PendingProposal);
        Assert.Equal("history.md", state.Canonization?.PendingProposal?.TargetFilePath);
    }
}
