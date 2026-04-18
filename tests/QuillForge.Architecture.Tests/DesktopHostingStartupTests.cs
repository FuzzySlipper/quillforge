using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Web;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Hosting;

namespace QuillForge.Architecture.Tests;

public sealed class DesktopHostingStartupTests
{
    [Fact]
    public void StartupPathResolver_WithConfiguredContentRoot_UsesExplicitOverride()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"quillforge-startup-override-{Guid.NewGuid():N}");
        var baseDir = Path.Combine(tempRoot, "published");
        var currentDir = Path.Combine(tempRoot, "working");
        var explicitContentRoot = Path.Combine(tempRoot, "Documents", "QuillForge");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(currentDir);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["QuillForge:ContentRoot"] = explicitContentRoot,
                })
                .Build();

            var paths = StartupPathResolver.Resolve(configuration, baseDir, currentDir);

            Assert.Equal(explicitContentRoot, paths.ContentRoot);
            Assert.Equal(StartupContentRootKind.ExplicitOverride, paths.ContentRootKind);
            Assert.Equal(Path.Combine(baseDir, "app-docs"), paths.DocsRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void StartupPathResolver_DesktopPublishedLaunch_UsesDocumentsWorkspaceAndPlansImport()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"quillforge-desktop-workspace-{Guid.NewGuid():N}");
        var baseDir = Path.Combine(tempRoot, "published");
        var currentDir = Path.Combine(tempRoot, "working");
        var documentsDir = Path.Combine(tempRoot, "Documents");
        var legacyUserRoot = Path.Combine(baseDir, "user");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(Path.Combine(legacyUserRoot, ContentPaths.Lore));
        File.WriteAllText(Path.Combine(legacyUserRoot, ContentPaths.ConfigFile), "models: {}\n");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["QuillForge:Startup:DesktopMode"] = "true",
                })
                .Build();

            var paths = StartupPathResolver.Resolve(configuration, baseDir, currentDir, documentsDir);

            Assert.Equal(Path.Combine(documentsDir, "QuillForge"), paths.ContentRoot);
            Assert.Equal(StartupContentRootKind.DesktopDefaultDocuments, paths.ContentRootKind);
            Assert.NotNull(paths.MigrationPlan);
            Assert.Equal(legacyUserRoot, paths.MigrationPlan!.SourceContentRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void StartupPathResolver_DesktopModeFromSourceTree_KeepsRepoUserBehavior()
    {
        var solutionRoot = StartupPathResolver.FindSolutionRoot(Directory.GetCurrentDirectory());
        Assert.NotNull(solutionRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QuillForge:Startup:DesktopMode"] = "true",
            })
            .Build();

        var paths = StartupPathResolver.Resolve(configuration, solutionRoot!, solutionRoot!);

        Assert.Equal(Path.Combine(solutionRoot!, "user"), paths.ContentRoot);
        Assert.Equal(StartupContentRootKind.SourceDevelopment, paths.ContentRootKind);
        Assert.Null(paths.MigrationPlan);
    }

    [Fact]
    public void StartupPathResolver_DesktopPublishedLaunch_DoesNotPlanImport_WhenDocumentsWorkspaceAlreadyHasContent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"quillforge-desktop-existing-{Guid.NewGuid():N}");
        var baseDir = Path.Combine(tempRoot, "published");
        var currentDir = Path.Combine(tempRoot, "working");
        var documentsDir = Path.Combine(tempRoot, "Documents");
        var desktopWorkspace = Path.Combine(documentsDir, "QuillForge");
        var legacyUserRoot = Path.Combine(baseDir, "user");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(desktopWorkspace);
        Directory.CreateDirectory(Path.Combine(legacyUserRoot, ContentPaths.Lore));
        File.WriteAllText(Path.Combine(legacyUserRoot, ContentPaths.ConfigFile), "models: {}\n");
        File.WriteAllText(Path.Combine(desktopWorkspace, "notes.txt"), "already using desktop workspace");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["QuillForge:Startup:DesktopMode"] = "true",
                })
                .Build();

            var paths = StartupPathResolver.Resolve(configuration, baseDir, currentDir, documentsDir);

            Assert.Equal(desktopWorkspace, paths.ContentRoot);
            Assert.Equal(StartupContentRootKind.DesktopDefaultDocuments, paths.ContentRootKind);
            Assert.Null(paths.MigrationPlan);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void BackendLaunchArgumentParser_NormalizesCustomDesktopArgs()
    {
        var result = BackendLaunchArgumentParser.Parse(
        [
            "--desktop-mode",
            "--content-root", "/tmp/quillforge",
            "--bind-mode=loopback",
            "--port", "42319",
            "--desktop-instance-id", "desktop-123",
            "--open-browser", "false",
            "--environment", "Development",
        ]);

        Assert.Equal(["--environment", "Development"], result.PassThroughArgs);
        Assert.Equal("True", result.ConfigurationOverrides["QuillForge:Startup:DesktopMode"]);
        Assert.Equal("/tmp/quillforge", result.ConfigurationOverrides["QuillForge:ContentRoot"]);
        Assert.Equal("loopback", result.ConfigurationOverrides["QuillForge:Startup:BindMode"]);
        Assert.Equal("42319", result.ConfigurationOverrides["QuillForge:Startup:Port"]);
        Assert.Equal("desktop-123", result.ConfigurationOverrides["QuillForge:Startup:DesktopInstanceId"]);
        Assert.Equal("false", result.ConfigurationOverrides["QuillForge:Startup:OpenBrowser"]);
    }

    [Fact]
    public void BackendHostingConfiguration_DesktopModeDefaultsToLoopbackBinding()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Http:Url"] = "http://0.0.0.0:8015",
                ["QuillForge:Startup:DesktopMode"] = "true",
            })
            .Build();

        var options = BackendLaunchOptions.FromConfiguration(configuration);
        var binding = BackendHostingConfiguration.Resolve(configuration, options);

        Assert.Equal(BackendBindMode.Loopback, binding.BindMode);
        Assert.Equal(8015, binding.Port);
        Assert.Equal("http://127.0.0.1:8015", binding.Url);
    }

    [Fact]
    public void DesktopWorkspaceMigrator_ImportsLegacyPortableWorkspace_WhenDocumentsWorkspaceIsEmpty()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"quillforge-migrate-workspace-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(tempRoot, "portable-user");
        var targetRoot = Path.Combine(tempRoot, "Documents", "QuillForge");
        Directory.CreateDirectory(Path.Combine(sourceRoot, ContentPaths.LoreDefault));
        Directory.CreateDirectory(Path.Combine(sourceRoot, ContentPaths.Data));
        File.WriteAllText(Path.Combine(sourceRoot, ContentPaths.ConfigFile), "models: {}\n");
        File.WriteAllText(Path.Combine(sourceRoot, ContentPaths.LoreDefault, "world.md"), "legacy lore");

        try
        {
            var result = DesktopWorkspaceMigrator.ImportIfNeeded(
                new WorkspaceMigrationPlan(sourceRoot, targetRoot),
                NullLogger.Instance);

            Assert.NotNull(result);
            Assert.Equal(2, result!.CopiedFileCount);
            Assert.True(File.Exists(Path.Combine(targetRoot, ContentPaths.ConfigFile)));
            Assert.True(File.Exists(Path.Combine(targetRoot, ContentPaths.LoreDefault, "world.md")));
            Assert.True(File.Exists(Path.Combine(sourceRoot, ContentPaths.ConfigFile)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DesktopWorkspaceMigrator_DoesNothing_WhenNoPlanIsProvided()
    {
        var result = DesktopWorkspaceMigrator.ImportIfNeeded(null, NullLogger.Instance);
        Assert.Null(result);
    }

    [Fact]
    public async Task HealthEndpoints_ReportLiveAndReadyStatesForDesktopMode()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(new BackendRuntimeInfo(
            DesktopMode: true,
            ContentRoot: "/tmp/quillforge",
            BindMode: BackendBindMode.Loopback,
            Port: 42319,
            DesktopInstanceId: "desktop-123",
            OpenBrowser: false,
            HttpUrl: "http://127.0.0.1:42319"));
        builder.Services.AddSingleton<StartupReadinessState>();

        var app = builder.Build();
        app.MapHealthEndpoints();

        var starting = await InvokeGetAsync(app, "/api/health/ready");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, starting.StatusCode);
        using (var startingDocument = JsonDocument.Parse(starting.Body))
        {
            Assert.Equal("starting", startingDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal("desktop", startingDocument.RootElement.GetProperty("mode").GetString());
            Assert.Equal("loopback", startingDocument.RootElement.GetProperty("bindMode").GetString());
            Assert.Equal(42319, startingDocument.RootElement.GetProperty("port").GetInt32());
        }

        app.Services.GetRequiredService<StartupReadinessState>().MarkReady();

        var live = await InvokeGetAsync(app, "/api/health/live");
        Assert.Equal(StatusCodes.Status200OK, live.StatusCode);
        using (var liveDocument = JsonDocument.Parse(live.Body))
        {
            Assert.Equal("live", liveDocument.RootElement.GetProperty("status").GetString());
        }

        var ready = await InvokeGetAsync(app, "/api/health/ready");
        Assert.Equal(StatusCodes.Status200OK, ready.StatusCode);
        using var readyDocument = JsonDocument.Parse(ready.Body);
        Assert.Equal("ready", readyDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("desktop-123", readyDocument.RootElement.GetProperty("desktopInstanceId").GetString());
    }

    private static async Task<(int StatusCode, string Body)> InvokeGetAsync(WebApplication app, string route)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate =>
                RouteMatches(candidate.RoutePattern, route)
                && EndpointSupportsMethod(candidate, "GET"));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = "GET";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        context.Response.Body = new MemoryStream();

        var requestDelegate = endpoint.RequestDelegate;
        Assert.NotNull(requestDelegate);
        await requestDelegate(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static bool RouteMatches(RoutePattern routePattern, string route)
    {
        return string.Equals('/' + routePattern.RawText?.TrimStart('/'), route, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndpointSupportsMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        return metadata is null || metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }
}
