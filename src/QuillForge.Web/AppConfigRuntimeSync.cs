using QuillForge.Core.Models;

namespace QuillForge.Web;

/// <summary>
/// Keeps the long-lived in-memory AppConfig instance aligned with the latest
/// persisted configuration after store-owned update operations succeed.
/// </summary>
public static class AppConfigRuntimeSync
{
    public static void CopyFrom(AppConfig target, AppConfig source)
    {
        target.Models = source.Models;
        target.Profiles = source.Profiles;
        target.Persona = source.Persona;
        target.NarrativeRules = source.NarrativeRules;
        target.Lore = source.Lore;
        target.WritingStyle = source.WritingStyle;
        target.Layout = source.Layout;
        target.Roleplay = source.Roleplay;
        target.Forge = source.Forge;
        target.WebSearch = source.WebSearch;
        target.Email = source.Email;
        target.Diagnostics = source.Diagnostics;
        target.Agents = source.Agents;
        target.Timeouts = source.Timeouts;
        target.ImageGen = source.ImageGen;
        target.Tts = source.Tts;
    }
}
