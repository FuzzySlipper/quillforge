using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.Registry;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", async (
            HttpContext httpContext,
            ISessionProfileReadService profileReadService,
            AutoUpdateService updateService,
            AppConfig config,
            ILoreStore loreStore,
            IConductorStore conductorStore,
            ISessionStore sessionStore,
            ProviderRegistry providerRegistry,
            CancellationToken ct) =>
        {
            var sessionId = httpContext.TryGetSessionId();
            var readView = await profileReadService.LoadAsync(sessionId, ct);
            var chatState = readView.SessionState;
            var conversationTurns = 0;
            var historyTokens = 0;

            if (sessionId.HasValue)
            {
                try
                {
                    var tree = await sessionStore.LoadAsync(sessionId.Value, ct);
                    var thread = tree.ToFlatThread();
                    conversationTurns = thread.Count;
                    historyTokens = thread.Sum(node => node.Content.GetText().Length) / 4;
                }
                catch (FileNotFoundException)
                {
                    // No persisted conversation yet for this session.
                }
            }

            // Calculate real token/file counts
            var loreFiles = 0;
            var loreTokens = 0;
            try
            {
                var loreSet = await loreStore.LoadLoreSetAsync(readView.ActiveLoreSet, ct);
                loreFiles = loreSet.Count;
                loreTokens = loreSet.Values.Sum(v => v.Length) / 4; // rough token estimate
            }
            catch { /* lore set may not exist */ }

            var conductorTokens = 0;
            try
            {
                var conductorPrompt = await conductorStore.LoadAsync(readView.ActiveConductor, config.Persona.MaxTokens, ct);
                conductorTokens = conductorPrompt.Length / 4;
            }
            catch { /* conductor may not exist */ }

            var contextLimit = ResolveContextLimit(providerRegistry, config.Models.Orchestrator);

            return Results.Ok(new StatusResponse
            {
                Version = BuildInfo.Version,
                Build = BuildInfo.InformationalVersion,
                Mode = chatState.Mode.ActiveMode.ToWireString(),
                Project = chatState.Mode.ProjectName,
                File = chatState.Mode.CurrentFile,
                LoreSet = readView.ActiveLoreSet,
                Conductor = readView.ActiveConductor,
                WritingStyle = readView.ActiveWritingStyle,
                Model = config.Models.Orchestrator,
                Layout = config.Layout.Active,
                AiCharacter = readView.ActiveAiCharacter ?? "",
                UserCharacter = readView.ActiveUserCharacter ?? "",
                ConversationTurns = conversationTurns,
                LoreFiles = loreFiles,
                ContextLimit = contextLimit,
                LoreTokens = loreTokens,
                ConductorTokens = conductorTokens,
                HistoryTokens = historyTokens,
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

    private static int ResolveContextLimit(ProviderRegistry providerRegistry, string configuredModel)
    {
        var directConfig = providerRegistry.GetConfig(configuredModel);
        if (directConfig?.ContextLimit is int directLimit)
        {
            return directLimit;
        }

        var defaultConfig = providerRegistry.GetAllConfigs().FirstOrDefault();
        return defaultConfig?.ContextLimit ?? 0;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }
}
