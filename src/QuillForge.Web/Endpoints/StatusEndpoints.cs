using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", (
            OrchestratorAgent orchestrator,
            AutoUpdateService updateService,
            AppConfig config) =>
        {
            return Results.Ok(new
            {
                status = "ready",
                version = BuildInfo.Version,
                build = BuildInfo.InformationalVersion,
                mode = orchestrator.ActiveModeName,
                project = orchestrator.ProjectName,
                file = orchestrator.CurrentFile,
                lore_set = config.Lore.Active,
                persona = config.Persona.Active,
                writing_style = config.WritingStyle.Active,
                model = config.Models.Orchestrator,
                layout = config.Layout.Active,
                ai_character = config.Roleplay.AiCharacter ?? "",
                user_character = config.Roleplay.UserCharacter ?? "",
                conversation_turns = 0,
                lore_files = 0,
                context_limit = 0,
                lore_tokens = 0,
                persona_tokens = 0,
                history_tokens = 0,
                update = updateService.UpdateAvailable ? new
                {
                    available = true,
                    version = updateService.LatestVersion,
                    url = updateService.DownloadUrl,
                } : null,
            });
        });

        app.MapGet("/api/debug", async (
            IEnumerable<IDiagnosticSource> sources,
            CancellationToken ct) =>
        {
            var result = new Dictionary<string, object>
            {
                ["build"] = new
                {
                    version = BuildInfo.Version,
                    informationalVersion = BuildInfo.InformationalVersion,
                    buildDate = BuildInfo.BuildDate,
                    buildAge = FormatDuration(BuildInfo.Age),
                    startTime = BuildInfo.StartTime,
                    uptime = FormatDuration(BuildInfo.Uptime),
                },
            };

            foreach (var source in sources)
            {
                result[source.Category] = await source.GetDiagnosticsAsync(ct);
            }

            return Results.Ok(result);
        });
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }
}
