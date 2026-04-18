using Microsoft.Extensions.Configuration;

namespace QuillForge.Web.Hosting;

internal sealed record BackendEndpointBinding(string Url, BackendBindMode BindMode, int Port);

internal static class BackendHostingConfiguration
{
    private const int DefaultHttpPort = 8015;

    public static void ApplyOverrides(ConfigurationManager configuration, BackendLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(launchOptions);

        if (!launchOptions.DesktopMode && launchOptions.BindMode is null && launchOptions.Port is null)
        {
            return;
        }

        var binding = Resolve(configuration, launchOptions);
        configuration["Kestrel:Endpoints:Http:Url"] = binding.Url;
    }

    public static BackendEndpointBinding Resolve(IConfiguration configuration, BackendLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(launchOptions);

        var configuredUri = TryParseHttpUri(configuration["Kestrel:Endpoints:Http:Url"]);
        var bindMode = launchOptions.ResolveRequestedBindMode() ?? InferBindMode(configuredUri?.Host);
        var port = launchOptions.Port ?? configuredUri?.Port ?? DefaultHttpPort;
        var host = bindMode == BackendBindMode.Loopback ? "127.0.0.1" : "0.0.0.0";
        return new BackendEndpointBinding($"http://{host}:{port}", bindMode, port);
    }

    private static BackendBindMode InferBindMode(string? host)
    {
        if (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
        {
            return BackendBindMode.Loopback;
        }

        return BackendBindMode.Lan;
    }

    private static Uri? TryParseHttpUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri;
    }
}
