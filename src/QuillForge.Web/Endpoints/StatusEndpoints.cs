using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", async (
            HttpContext httpContext,
            ISessionRuntimeService runtimeService,
            AutoUpdateService updateService,
            AppConfig config,
            ILoreStore loreStore,
            IPersonaStore personaStore,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var chatState = await runtimeService.LoadViewAsync(sessionId, ct);
            // Calculate real token/file counts
            var loreFiles = 0;
            var loreTokens = 0;
            try
            {
                var loreSet = await loreStore.LoadLoreSetAsync(config.Lore.Active, ct);
                loreFiles = loreSet.Count;
                loreTokens = loreSet.Values.Sum(v => v.Length) / 4; // rough token estimate
            }
            catch { /* lore set may not exist */ }

            var personaTokens = 0;
            try
            {
                var persona = await personaStore.LoadAsync(config.Persona.Active, config.Persona.MaxTokens, ct);
                personaTokens = persona.Length / 4;
            }
            catch { /* persona may not exist */ }

            return Results.Ok(new StatusResponse
            {
                Version = BuildInfo.Version,
                Build = BuildInfo.InformationalVersion,
                Mode = chatState.Mode.ActiveModeName,
                Project = chatState.Mode.ProjectName,
                File = chatState.Mode.CurrentFile,
                LoreSet = config.Lore.Active,
                Persona = config.Persona.Active,
                WritingStyle = config.WritingStyle.Active,
                Model = config.Models.Orchestrator,
                Layout = config.Layout.Active,
                AiCharacter = config.Roleplay.AiCharacter ?? "",
                UserCharacter = config.Roleplay.UserCharacter ?? "",
                ConversationTurns = 0, // requires active session tracking
                LoreFiles = loreFiles,
                ContextLimit = 0, // provider-specific, needs registry lookup
                LoreTokens = loreTokens,
                PersonaTokens = personaTokens,
                HistoryTokens = 0, // requires active session tracking
                DiagnosticsLivePanel = config.Diagnostics.LivePanel,
                Update = updateService.UpdateAvailable ? new UpdateInfoDto
                {
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
