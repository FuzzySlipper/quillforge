using System.Reflection;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Architecture.Tests;

/// <summary>
/// Architecture tests for the per-session runtime state hierarchy (Task 43).
/// Verifies type definitions, ownership boundaries, and persistence interfaces.
/// </summary>
public class SessionRuntimeStateTests
{
    [Fact]
    public void SessionRuntimeState_OwnsExactlyFourSubStates()
    {
        // The aggregate should have exactly Mode, Profile, Writer, and Narrative
        // sub-states
        // plus SessionId and LastModified metadata. No flat bag of nullable fields.
        var props = typeof(SessionRuntimeState).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var subStateProps = props.Where(p =>
            p.PropertyType == typeof(ModeSelectionState) ||
            p.PropertyType == typeof(ProfileState) ||
            p.PropertyType == typeof(WriterRuntimeState) ||
            p.PropertyType == typeof(NarrativeRuntimeState))
            .ToList();

        Assert.Equal(4, subStateProps.Count);
        Assert.Contains(subStateProps, p => p.Name == "Mode");
        Assert.Contains(subStateProps, p => p.Name == "Profile");
        Assert.Contains(subStateProps, p => p.Name == "Writer");
        Assert.Contains(subStateProps, p => p.Name == "Narrative");
    }

    [Fact]
    public void SubStates_AreNotNullByDefault()
    {
        var state = new SessionRuntimeState();
        Assert.NotNull(state.Mode);
        Assert.NotNull(state.Profile);
        Assert.NotNull(state.Writer);
        Assert.NotNull(state.Narrative);
    }

    [Fact]
    public void ModeSelectionState_DefaultsToGeneral()
    {
        var mode = new ModeSelectionState();
        Assert.Equal("general", mode.ActiveModeName);
        Assert.Null(mode.ProjectName);
        Assert.Null(mode.CurrentFile);
        Assert.Null(mode.Character);
    }

    [Fact]
    public void ProfileState_DefaultsToNull_MeaningUseDefaultProfileWithNoOverrides()
    {
        var profile = new ProfileState();
        Assert.Null(profile.ProfileId);
        Assert.Null(profile.ActivePersona);
        Assert.Null(profile.ActiveLoreSet);
        Assert.Null(profile.ActiveWritingStyle);
    }

    [Fact]
    public void WriterRuntimeState_DefaultsToIdle()
    {
        var writer = new WriterRuntimeState();
        Assert.Equal(WriterState.Idle, writer.State);
        Assert.Null(writer.PendingContent);
    }

    [Fact]
    public void ModeFields_DoNotExist_OnTopLevelAggregate()
    {
        // Ensure mode-specific fields aren't duplicated at the top level
        var topProps = typeof(SessionRuntimeState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.DoesNotContain("ActiveModeName", topProps);
        Assert.DoesNotContain("ProjectName", topProps);
        Assert.DoesNotContain("CurrentFile", topProps);
        Assert.DoesNotContain("Character", topProps);
        Assert.DoesNotContain("WriterPendingContent", topProps);
    }

    [Fact]
    public void ISessionRuntimeStore_ExistsInCore()
    {
        var storeType = typeof(ISessionRuntimeStore);
        Assert.True(storeType.IsInterface);
        Assert.Equal("QuillForge.Core", storeType.Assembly.GetName().Name);
    }

    [Fact]
    public void ISessionRuntimeStore_HasLoadSaveDelete()
    {
        var methods = typeof(ISessionRuntimeStore).GetMethods()
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Contains("LoadAsync", methods);
        Assert.Contains("SaveAsync", methods);
        Assert.Contains("DeleteAsync", methods);
    }

    [Fact]
    public void ISessionRuntimeService_ExistsInCore()
    {
        var serviceType = typeof(ISessionRuntimeService);
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
    public void SessionRuntimeState_FullyPopulated_HasExpectedValues()
    {
        var sessionId = Guid.NewGuid();
        var state = new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveModeName = "writer",
                ProjectName = "my-novel",
                CurrentFile = "chapter1.md",
                Character = "hero",
            },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActivePersona = "narrator",
                ActiveLoreSet = "fantasy",
                ActiveWritingStyle = "literary",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "pending text",
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
        };

        Assert.Equal(sessionId, state.SessionId);
        Assert.Equal("writer", state.Mode.ActiveModeName);
        Assert.Equal("my-novel", state.Mode.ProjectName);
        Assert.Equal("chapter1.md", state.Mode.CurrentFile);
        Assert.Equal("hero", state.Mode.Character);
        Assert.Equal("grim", state.Profile.ProfileId);
        Assert.Equal("narrator", state.Profile.ActivePersona);
        Assert.Equal("fantasy", state.Profile.ActiveLoreSet);
        Assert.Equal("literary", state.Profile.ActiveWritingStyle);
        Assert.Equal("pending text", state.Writer.PendingContent);
        Assert.Equal(WriterState.PendingReview, state.Writer.State);
        Assert.Equal("track the rising pressure", state.Narrative.DirectorNotes);
        Assert.Equal("arc-one", state.Narrative.ActivePlotFile);
        Assert.Equal("midpoint", state.Narrative.PlotProgress.CurrentBeat);
    }
}
