namespace QuillForge.Core.Services;

/// <summary>
/// Substitutes common roleplay shortcodes in user-authored character and prompt
/// material. Compatible with SillyTavern, Character.AI, and Janitor-style
/// character card shortcodes.
///
/// Supported shortcodes:
///   {{char}} — the active AI-played character name
///   {{user}} — the active user persona/seat name
///
/// Missing values are left unresolved so callers can log diagnostics rather
/// than silently guessing a replacement.
/// </summary>
public static class RoleplayShortcodeResolver
{
    /// <summary>
    /// Substitutes {{char}} and {{user}} in the given text. Unresolved shortcodes
    /// are preserved unchanged so the caller can warn the user or audit logs.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <param name="charName">The active AI character name. If null or empty, {{char}} is left as-is.</param>
    /// <param name="userName">The active user persona name. If null or empty, {{user}} is left as-is.</param>
    /// <returns>The text with supported shortcodes replaced where values are available.</returns>
    public static string Substitute(string text, string? charName, string? userName)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;

        if (!string.IsNullOrWhiteSpace(charName))
        {
            result = result.Replace("{{char}}", charName, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            result = result.Replace("{{user}}", userName, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Returns the unresolved shortcodes still present in the text after
    /// substitution, so callers can log deterministic warnings.
    /// </summary>
    public static IReadOnlyList<string> FindUnresolved(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var unresolved = new List<string>();
        if (text.Contains("{{char}}", StringComparison.Ordinal))
        {
            unresolved.Add("{{char}}");
        }

        if (text.Contains("{{user}}", StringComparison.Ordinal))
        {
            unresolved.Add("{{user}}");
        }

        return unresolved;
    }
}
