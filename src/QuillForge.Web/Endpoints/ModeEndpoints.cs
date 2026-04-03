using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.FileSystem;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class ModeEndpoints
{
    public static void MapModeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/mode", async (
            ISessionRuntimeService runtimeService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var state = await runtimeService.LoadViewAsync(sessionId, ct);
            return Results.Ok(new ModeResponse
            {
                Mode = state.Mode.ActiveModeName,
                Project = state.Mode.ProjectName,
                File = state.Mode.CurrentFile,
                Character = state.Mode.Character,
                PendingContent = state.Writer.PendingContent,
            });
        });

        app.MapPost("/api/mode", async (
            HttpContext httpContext,
            ISessionRuntimeService runtimeService,
            RuntimeStateStore legacyStore,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString() ?? "general";
            var project = root.TryGetProperty("project", out var p) ? p.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var character = root.TryGetProperty("character", out var c) ? c.GetString() : null;
            var sessionId = root.GetOptionalGuid("sessionId");

            var result = await runtimeService.SetModeAsync(
                sessionId,
                new SetSessionModeCommand(mode, project, file, character),
                ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            // Also persist to legacy store for backward compat during transition
            await legacyStore.SaveAsync(new RuntimeState
            {
                LastMode = mode,
                LastProject = project,
                LastFile = file,
                LastCharacter = character,
            }, ct);

            return Results.Ok(new ModeResponse
            {
                Mode = mode,
                Project = project,
                File = file,
                Character = character,
            });
        });

        app.MapGet("/api/profiles", async (
            HttpContext httpContext,
            IConductorStore conductorStore,
            ILoreStore loreStore,
            INarrativeRulesStore narrativeRulesStore,
            IWritingStyleStore styleStore,
            IProfileConfigService profileService,
            ISessionRuntimeService runtimeService,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var state = await runtimeService.LoadViewAsync(sessionId, ct);
            var profileIds = await profileService.ListAsync(ct);
            var defaultProfileId = await profileService.GetDefaultProfileIdAsync(ct);
            var conductors = await conductorStore.ListAsync(ct);
            var loreSets = await loreStore.ListLoreSetsAsync(ct);
            var narrativeRules = await narrativeRulesStore.ListAsync(ct);
            var styles = await styleStore.ListAsync(ct);

            return Results.Ok(new ProfilesResponse
            {
                ProfileIds = profileIds,
                DefaultProfileId = defaultProfileId,
                ActiveProfileId = state.Profile.ProfileId ?? defaultProfileId,
                Personas = conductors,
                LoreSets = loreSets,
                NarrativeRules = narrativeRules,
                WritingStyles = styles,
                ActivePersona = state.Profile.ActivePersona ?? "default",
                ActiveLore = state.Profile.ActiveLoreSet ?? "default",
                ActiveNarrativeRules = state.Profile.ActiveNarrativeRules ?? "default",
                ActiveWritingStyle = state.Profile.ActiveWritingStyle ?? "default",
            });
        });

        app.MapGet("/api/agents/models", (AppConfig config) =>
        {
            return Results.Ok(new AgentAssignmentsResponse
            {
                Assignments = new AgentModelAssignments
                {
                    Orchestrator = config.Models.Orchestrator,
                    ProseWriter = config.Models.ProseWriter,
                    Librarian = config.Models.Librarian,
                    ForgeWriter = config.Models.ForgeWriter,
                    ForgePlanner = config.Models.ForgePlanner,
                    ForgeReviewer = config.Models.ForgeReviewer,
                    Research = config.Models.Research,
                }
            });
        });

        app.MapPut("/api/agents/models", async (
            HttpContext httpContext,
            AppConfig runtimeConfig,
            IAppConfigStore configStore,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var orchestrator = root.TryGetProperty("orchestrator", out var o) ? o.GetString() : null;
            var proseWriter = root.TryGetProperty("proseWriter", out var pw) ? pw.GetString() : null;
            var librarian = root.TryGetProperty("librarian", out var lb) ? lb.GetString() : null;
            var forgeWriter = root.TryGetProperty("forgeWriter", out var fw) ? fw.GetString() : null;
            var forgePlanner = root.TryGetProperty("forgePlanner", out var fp) ? fp.GetString() : null;
            var forgeReviewer = root.TryGetProperty("forgeReviewer", out var fr) ? fr.GetString() : null;
            var research = root.TryGetProperty("research", out var rs) ? rs.GetString() : null;

            var updatedConfig = await configStore.UpdateAsync(current => current with
            {
                Models = current.Models with
                {
                    Orchestrator = orchestrator ?? current.Models.Orchestrator,
                    ProseWriter = proseWriter ?? current.Models.ProseWriter,
                    Librarian = librarian ?? current.Models.Librarian,
                    ForgeWriter = forgeWriter ?? current.Models.ForgeWriter,
                    ForgePlanner = forgePlanner ?? current.Models.ForgePlanner,
                    ForgeReviewer = forgeReviewer ?? current.Models.ForgeReviewer,
                    Research = research ?? current.Models.Research,
                }
            }, ct);
            AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);

            logger.LogInformation(
                "Agent models updated: orchestrator={Orch}, proseWriter={Prose}, librarian={Lib}",
                updatedConfig.Models.Orchestrator,
                updatedConfig.Models.ProseWriter,
                updatedConfig.Models.Librarian);

            return Results.Ok(new AgentAssignmentsUpdateResponse
            {
                Assignments = new AgentModelAssignments
                {
                    Orchestrator = updatedConfig.Models.Orchestrator,
                    ProseWriter = updatedConfig.Models.ProseWriter,
                    Librarian = updatedConfig.Models.Librarian,
                    ForgeWriter = updatedConfig.Models.ForgeWriter,
                    ForgePlanner = updatedConfig.Models.ForgePlanner,
                    ForgeReviewer = updatedConfig.Models.ForgeReviewer,
                    Research = updatedConfig.Models.Research,
                }
            });
        });
    }
}
