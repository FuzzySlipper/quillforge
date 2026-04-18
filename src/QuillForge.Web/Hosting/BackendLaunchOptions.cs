using Microsoft.Extensions.Configuration;

namespace QuillForge.Web.Hosting;

internal enum BackendBindMode
{
    Loopback,
    Lan,
}

internal sealed record BackendLaunchOptions(
    bool DesktopMode,
    BackendBindMode? BindMode,
    int? Port,
    string? DesktopInstanceId,
    bool OpenBrowser)
{
    public static BackendLaunchOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var desktopMode = configuration.GetValue<bool>("QuillForge:Startup:DesktopMode");

        BackendBindMode? bindMode = null;
        var bindModeText = configuration["QuillForge:Startup:BindMode"];
        if (!string.IsNullOrWhiteSpace(bindModeText))
        {
            bindMode = ParseBindMode(bindModeText);
        }

        int? port = null;
        var portText = configuration["QuillForge:Startup:Port"];
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out var parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                throw new InvalidOperationException($"Invalid startup port '{portText}'. Expected a value between 1 and 65535.");
            }

            port = parsedPort;
        }

        var openBrowser = configuration.GetValue<bool?>("QuillForge:Startup:OpenBrowser") ?? false;
        return new BackendLaunchOptions(
            desktopMode,
            bindMode,
            port,
            configuration["QuillForge:Startup:DesktopInstanceId"],
            openBrowser);
    }

    public BackendBindMode? ResolveRequestedBindMode()
    {
        return BindMode ?? (DesktopMode ? BackendBindMode.Loopback : null);
    }

    private static BackendBindMode ParseBindMode(string value)
    {
        if (string.Equals(value, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return BackendBindMode.Loopback;
        }

        if (string.Equals(value, "lan", StringComparison.OrdinalIgnoreCase))
        {
            return BackendBindMode.Lan;
        }

        throw new InvalidOperationException($"Invalid bind mode '{value}'. Expected 'loopback' or 'lan'.");
    }
}
