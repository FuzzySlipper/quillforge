using Microsoft.AspNetCore.Http;
using QuillForge.Core;
using QuillForge.Web.Contracts;
using QuillForge.Web.Hosting;

namespace QuillForge.Web.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health/live", (BackendRuntimeInfo runtimeInfo) =>
            Results.Ok(CreateResponse("live", runtimeInfo)));

        app.MapGet("/api/health/ready", (StartupReadinessState readinessState, BackendRuntimeInfo runtimeInfo) =>
        {
            var status = readinessState.IsReady ? "ready" : "starting";
            var response = CreateResponse(status, runtimeInfo);
            return readinessState.IsReady
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    private static HealthResponse CreateResponse(string status, BackendRuntimeInfo runtimeInfo)
    {
        return new HealthResponse
        {
            Status = status,
            Version = BuildInfo.Version,
            Build = BuildInfo.InformationalVersion,
            Mode = runtimeInfo.Mode,
            BindMode = runtimeInfo.BindMode == BackendBindMode.Loopback ? "loopback" : "lan",
            ContentRoot = runtimeInfo.ContentRoot,
            Port = runtimeInfo.Port,
            DesktopInstanceId = runtimeInfo.DesktopInstanceId,
        };
    }
}
