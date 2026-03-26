using QuillForge.Core.Agents;
using QuillForge.Core.Services;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", (
            OrchestratorAgent orchestrator,
            AutoUpdateService updateService) =>
        {
            return Results.Ok(new
            {
                status = "ok",
                mode = orchestrator.ActiveModeName,
                project = orchestrator.ProjectName,
                file = orchestrator.CurrentFile,
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
            var result = new Dictionary<string, object>();
            foreach (var source in sources)
            {
                result[source.Category] = await source.GetDiagnosticsAsync(ct);
            }
            return Results.Ok(result);
        });
    }
}
