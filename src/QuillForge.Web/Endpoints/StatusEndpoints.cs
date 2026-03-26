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
                Status = "ready",
                Version = BuildInfo.Version,
                Build = BuildInfo.InformationalVersion,
                Mode = orchestrator.ActiveModeName,
                Project = orchestrator.ProjectName,
                File = orchestrator.CurrentFile,
                LoreSet = config.Lore.Active,
                Persona = config.Persona.Active,
                WritingStyle = config.WritingStyle.Active,
                Model = config.Models.Orchestrator,
                Layout = config.Layout.Active,
                AiCharacter = config.Roleplay.AiCharacter ?? "",
                UserCharacter = config.Roleplay.UserCharacter ?? "",
                ConversationTurns = 0,
                LoreFiles = 0,
                ContextLimit = 0,
                LoreTokens = 0,
                PersonaTokens = 0,
                HistoryTokens = 0,
                Update = updateService.UpdateAvailable ? new
                {
                    Available = true,
                    Version = updateService.LatestVersion,
                    Url = updateService.DownloadUrl,
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
                    Version = BuildInfo.Version,
                    InformationalVersion = BuildInfo.InformationalVersion,
                    BuildDate = BuildInfo.BuildDate,
                    BuildAge = FormatDuration(BuildInfo.Age),
                    StartTime = BuildInfo.StartTime,
                    Uptime = FormatDuration(BuildInfo.Uptime),
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
