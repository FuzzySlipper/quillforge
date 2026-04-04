using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class ModeEndpoints
{
    public static void MapModeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/mode", async (
            ISessionStateService runtimeService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var state = await runtimeService.LoadViewAsync(sessionId, ct);
            return Results.Ok(new ModeResponse
            {
                SessionId = state.SessionId,
                Mode = state.Mode.ActiveModeName,
                Project = state.Mode.ProjectName,
                File = state.Mode.CurrentFile,
                Character = state.Mode.Character,
                PendingContent = state.Writer.PendingContent,
            });
        });

        app.MapPost("/api/mode", async (
            HttpContext httpContext,
            ISessionStateService runtimeService,
            ISessionBootstrapService bootstrapService,
            ISessionLifecycleService lifecycleService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString() ?? "general";
            var project = root.TryGetProperty("project", out var p) ? p.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var character = root.TryGetProperty("character", out var c) ? c.GetString() : null;
            var sessionId = root.GetOptionalGuid("sessionId");
            Guid? createdSessionId = null;

            if (!sessionId.HasValue)
            {
                var tree = await bootstrapService.CreateAsync(
                    new CreateSessionCommand
                    {
                        Name = "New Session",
                    },
                    ct);
                sessionId = tree.SessionId;
                createdSessionId = tree.SessionId;
            }

            var result = await runtimeService.SetModeAsync(
                sessionId,
                new SetSessionModeCommand(mode, project, file, character),
                ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                if (createdSessionId.HasValue)
                {
                    await lifecycleService.DeleteAsync(createdSessionId.Value, ct);
                }

                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                if (createdSessionId.HasValue)
                {
                    await lifecycleService.DeleteAsync(createdSessionId.Value, ct);
                }

                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            var state = result.Value!;

            return Results.Ok(new ModeResponse
            {
                SessionId = state.SessionId,
                Mode = state.Mode.ActiveModeName,
                Project = state.Mode.ProjectName,
                File = state.Mode.CurrentFile,
                Character = state.Mode.Character,
                PendingContent = state.Writer.PendingContent,
            });
        });

        app.MapGet("/api/profiles", async (
            HttpContext httpContext,
            ISessionProfileReadService profileReadService,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            return Results.Ok(await profileReadService.BuildProfilesResponseAsync(sessionId, ct));
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
