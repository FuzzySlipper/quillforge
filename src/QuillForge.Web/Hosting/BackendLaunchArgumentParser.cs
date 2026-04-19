using Microsoft.Extensions.Configuration;

namespace QuillForge.Web.Hosting;

internal sealed record BackendLaunchArgumentParseResult(
    string[] PassThroughArgs,
    IReadOnlyDictionary<string, string?> ConfigurationOverrides);

internal static class BackendLaunchArgumentParser
{
    private static readonly IReadOnlyDictionary<string, string> SwitchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["--desktop-mode"] = "QuillForge:Startup:DesktopMode",
        ["--content-root"] = "QuillForge:ContentRoot",
        ["--runtime-root"] = "QuillForge:Startup:RuntimeRoot",
        ["--bind-mode"] = "QuillForge:Startup:BindMode",
        ["--port"] = "QuillForge:Startup:Port",
        ["--desktop-instance-id"] = "QuillForge:Startup:DesktopInstanceId",
        ["--open-browser"] = "QuillForge:Startup:OpenBrowser",
    };

    private static readonly HashSet<string> BooleanSwitches = new(StringComparer.OrdinalIgnoreCase)
    {
        "--desktop-mode",
        "--open-browser",
    };

    public static BackendLaunchArgumentParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var passThrough = new List<string>();
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!TryMatchCustomSwitch(argument, out var switchName, out var inlineValue))
            {
                passThrough.Add(argument);
                continue;
            }

            var isBooleanSwitch = BooleanSwitches.Contains(switchName);
            string? value;

            if (isBooleanSwitch)
            {
                if (inlineValue is not null)
                {
                    value = inlineValue;
                }
                else if (index + 1 < args.Length && IsBooleanLiteral(args[index + 1]))
                {
                    value = args[++index];
                }
                else
                {
                    value = bool.TrueString;
                }
            }
            else
            {
                value = inlineValue;
                if (value is null)
                {
                    if (index + 1 >= args.Length || LooksLikeSwitch(args[index + 1]))
                    {
                        throw new InvalidOperationException($"Missing value for startup argument '{switchName}'.");
                    }

                    value = args[++index];
                }
            }

            overrides[SwitchMappings[switchName]] = value;
        }

        return new BackendLaunchArgumentParseResult(passThrough.ToArray(), overrides);
    }

    private static bool TryMatchCustomSwitch(string argument, out string switchName, out string? inlineValue)
    {
        foreach (var candidate in SwitchMappings.Keys)
        {
            if (string.Equals(argument, candidate, StringComparison.OrdinalIgnoreCase))
            {
                switchName = candidate;
                inlineValue = null;
                return true;
            }

            var prefix = candidate + "=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                switchName = candidate;
                inlineValue = argument[prefix.Length..];
                return true;
            }
        }

        switchName = string.Empty;
        inlineValue = null;
        return false;
    }

    private static bool LooksLikeSwitch(string argument)
    {
        return argument.StartsWith("--", StringComparison.Ordinal);
    }

    private static bool IsBooleanLiteral(string argument)
    {
        return bool.TryParse(argument, out _);
    }
}
