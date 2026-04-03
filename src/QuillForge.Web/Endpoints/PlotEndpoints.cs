using System.Text;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class PlotEndpoints
{
    public static void MapPlotEndpoints(this WebApplication app)
    {
        app.MapGet("/api/plots", async (
            HttpContext httpContext,
            IPlotStore plotStore,
            ISessionRuntimeService runtimeService,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var runtimeState = await runtimeService.LoadViewAsync(sessionId, ct);
            var names = await plotStore.ListAsync(ct);
            var files = new List<PlotFileDto>();

            foreach (var name in names)
            {
                var content = await plotStore.LoadAsync(name, ct);
                files.Add(new PlotFileDto
                {
                    Name = name,
                    Path = $"{name}.md",
                    Tokens = content.Length / 4,
                    Size = content.Length,
                });
            }

            return Results.Ok(new PlotListResponse
            {
                Files = files,
                ActivePlotFile = runtimeState.Narrative.ActivePlotFile,
                SessionId = sessionId,
                PlotProgress = new PlotProgressDto
                {
                    CurrentBeat = runtimeState.Narrative.PlotProgress.CurrentBeat,
                    CompletedBeats = runtimeState.Narrative.PlotProgress.CompletedBeats,
                    Deviations = runtimeState.Narrative.PlotProgress.Deviations,
                },
            });
        });

        app.MapGet("/api/plots/{name}", async (
            string name,
            IPlotStore plotStore,
            CancellationToken ct) =>
        {
            var content = await plotStore.LoadAsync(name, ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                return Results.NotFound(new { Error = $"Plot {name} not found" });
            }

            return Results.Ok(new PlotReadResponse
            {
                Name = name,
                Content = content,
                Tokens = content.Length / 4,
            });
        });

        app.MapPost("/api/plots/generate", async (
            PlotGenerateRequest request,
            NarrativeDirectorAgent narrativeDirector,
            ISessionRuntimeService runtimeService,
            IInteractiveSessionContextService sessionContextService,
            IPlotStore plotStore,
            AppConfig appConfig,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var runtimeState = await runtimeService.LoadViewAsync(request.SessionId, ct);
            var sessionContext = await sessionContextService.BuildAsync(runtimeState, ct);
            var context = new AgentContext
            {
                SessionId = runtimeState.SessionId ?? Guid.CreateVersion7(),
                ActiveMode = runtimeState.Mode.ActiveModeName,
                ActiveLoreSet = runtimeState.Profile.ActiveLoreSet ?? appConfig.Lore.Active,
                ActiveNarrativeRules = runtimeState.Profile.ActiveNarrativeRules ?? appConfig.NarrativeRules.Active,
                ActiveWritingStyle = runtimeState.Profile.ActiveWritingStyle ?? appConfig.WritingStyle.Active,
                SessionContext = sessionContext,
            };

            var result = await narrativeDirector.GeneratePlotAsync(
                new PlotGenerationRequest { Prompt = request.Prompt },
                context,
                ct);

            var name = await ChoosePlotNameAsync(plotStore, request.Prompt, result.Markdown, ct);
            await plotStore.SaveAsync(name, result.Markdown, ct);

            logger.LogInformation(
                "Generated plot file {PlotName} for session {SessionId}",
                name,
                request.SessionId);

            return Results.Ok(new PlotGenerateResponse
            {
                Name = name,
                Content = result.Markdown,
                ToolRoundsUsed = result.ToolRoundsUsed,
                SessionId = request.SessionId,
            });
        });

        app.MapPost("/api/plots/load", async (
            PlotLoadRequest request,
            IPlotStore plotStore,
            ISessionRuntimeService runtimeService,
            CancellationToken ct) =>
        {
            if (!await plotStore.ExistsAsync(request.Name, ct))
            {
                return Results.NotFound(new { Error = $"Plot {request.Name} not found" });
            }

            var result = await runtimeService.SetActivePlotAsync(
                request.SessionId,
                new SetActivePlotCommand(request.Name),
                ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new { Error = "session_busy", Message = result.Error });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                return Results.BadRequest(new { Error = "invalid_session_mutation", Message = result.Error });
            }

            return Results.Ok(new PlotMutationResponse
            {
                SessionId = request.SessionId,
                ActivePlotFile = result.Value!.Narrative.ActivePlotFile,
            });
        });

        app.MapPost("/api/plots/unload", async (
            PlotUnloadRequest request,
            ISessionRuntimeService runtimeService,
            CancellationToken ct) =>
        {
            var result = await runtimeService.ClearActivePlotAsync(request.SessionId, ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new { Error = "session_busy", Message = result.Error });
            }

            return Results.Ok(new PlotMutationResponse
            {
                SessionId = request.SessionId,
                ActivePlotFile = result.Value?.Narrative.ActivePlotFile,
            });
        });
    }

    private static async Task<string> ChoosePlotNameAsync(
        IPlotStore plotStore,
        string? prompt,
        string markdown,
        CancellationToken ct)
    {
        var seed = !string.IsNullOrWhiteSpace(prompt)
            ? prompt
            : TryExtractTitle(markdown) ?? $"plot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        var slug = Slugify(seed);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = $"plot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        }

        var candidate = slug;
        var suffix = 2;
        while (await plotStore.ExistsAsync(candidate, ct))
        {
            candidate = $"{slug}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string? TryExtractTitle(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        var previousDash = false;

        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
