using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Fixtures;

/// <summary>
/// Defines the Xavier/Caleb lore content and probe prompts for live LLM-backed
/// lore consistency testing. Uses the actual lore context that would be available
/// to a real roleplay agent pipeline, with Xavier-facing probe questions designed
/// to trigger Caleb lore contamination if the pipeline leaks off-character facts.
/// </summary>
public static class LiveXavierCalebScenario
{
    /// <summary>Xavier's known lore facts (simulates character/xavier.md).</summary>
    public static IReadOnlyList<string> XavierLore { get; } =
    [
        "Xavier is a Deepspace Hunter in the Division.",
        "He has silver-streaked black hair and sharp grey eyes.",
        "He carries a standard-issue hunter carbine and combat knife.",
        "He has standard Division neural interface augmentation.",
        "He has a faint scar above his left eyebrow from a Deepspace patrol.",
        "Xavier's equipment is standard Division-issue — well-worn and practical.",
    ];

    /// <summary>Caleb's known lore facts (simulates characters/caleb.md).</summary>
    public static IReadOnlyList<string> CalebLore { get; } =
    [
        "Caleb has a custom prosthetic arm with advanced combat functionality.",
        "His Toring Chip interfaces directly with the Division tactical network.",
        "Caleb wears a dark Division field coat.",
        "He has close-cropped dark hair.",
        "His prosthetic arm has visible combat plating.",
    ];

    /// <summary>Shared world body-tech context (simulates world/body-tech.md).</summary>
    public static IReadOnlyList<string> SharedBodyTech { get; } =
    [
        "Standard Division neural interfaces are common among all hunter personnel.",
        "Hunter-grade tactical gear is standard issue for all operatives.",
        "Neural interfaces provide enhanced situational awareness in the field.",
    ];

    /// <summary>
    /// System prompt for the roleplay agent pipeline (LibrarianAgent / query_lore role).
    /// Instructs the agent about Xavier as the active character and Caleb as off-character
    /// whose personal details must not be asserted about Xavier.
    /// </summary>
    public static string BuildSystemPrompt(string activeCharacter, string offCharacter)
    {
        return $$"""
You are a roleplay lore retrieval and narrative generation system.

ACTIVE CHARACTER: {{activeCharacter}}
OFF-CHARACTER (do not attribute their details to the active character): {{offCharacter}}

Your role:
- You provide lore about the active character when queried.
- You may reference shared world knowledge that applies to all characters.
- You MUST NOT attribute off-character personal details to the active character.
- Explicit comparisons between characters are allowed.

Lore sources:
""";
    }

    /// <summary>
    /// Probe prompts designed to trigger Caleb lore contamination in Xavier context.
    /// Each prompt targets a specific contamination boundary.
    /// </summary>
    public static IReadOnlyList<LiveProbeTurn> ProbeTurns { get; } =
    [
        new LiveProbeTurn
        {
            TurnNumber = 1,
            Category = "appearance",
            UserMessage = "Describe Xavier's appearance — his hair, eyes, build, and any distinctive features.",
            ExpectedSubject = "Xavier",
            ContaminationRisk = "Caleb prosthetic arm or Toring Chip mentioned as Xavier's features",
            ProbePrompt = """
You are answering as the Narrative Director / query_lore component.
You have access to the lore listed below.
Answer the user's question about Xavier's appearance using ONLY Xavier-specific lore.
Do NOT mention Caleb's prosthetic arm, Toring Chip, or dark coat as if they belong to Xavier.
""",
        },
        new LiveProbeTurn
        {
            TurnNumber = 2,
            Category = "gear_and_augmentations",
            UserMessage = "Tell me about Xavier's gear, weapons, and any augmentations or body tech he has.",
            ExpectedSubject = "Xavier",
            ContaminationRisk = "Caleb prosthetic arm, Toring Chip, or custom combat augments described as Xavier's",
            ProbePrompt = """
You are answering as the query_lore / LibrarianAgent component.
You are retrieving facts about Xavier's equipment and augmentations.
Caleb has a custom prosthetic combat arm and a Toring Chip neural interface.
These are CALEB's personal details and MUST NOT be attributed to Xavier.
Xavier uses standard-issue Division gear and a standard neural interface like all operatives.
""",
        },
        new LiveProbeTurn
        {
            TurnNumber = 3,
            Category = "body_tech",
            UserMessage = "What body modifications or cybernetic augmentations does Xavier have? Describe them.",
            ExpectedSubject = "Xavier",
            ContaminationRisk = "Caleb prosthetic arm or Toring Chip described as Xavier's augmentations",
            ProbePrompt = """
You are answering as the ProseWriter / direct_scene component.
You are writing a descriptive scene about Xavier's augmentations.
Standard Division neural interfaces are common to all operatives — do not make Xavier's seem unique.
Caleb's prosthetic combat arm and Toring Chip are NOT Xavier's equipment.
If Xavier has no unique body modifications, say so directly.
""",
        },
        new LiveProbeTurn
        {
            TurnNumber = 4,
            Category = "backstory",
            UserMessage = "Tell me about Xavier's background, his Division career, and what makes him unique as a Deepspace Hunter.",
            ExpectedSubject = "Xavier",
            ContaminationRisk = "Caleb backstory elements or prosthetic origin story attributed to Xavier",
            ProbePrompt = """
You are answering as the Narrative Director / scene_brief component.
Focus on Xavier's background as a Deepspace Hunter in the Division.
Do NOT fabricate backstory details from Caleb's character (prosthetic, Toring Chip, custom augments).
Xavier is a standard Division operative with standard gear.
""",
        },
        new LiveProbeTurn
        {
            TurnNumber = 5,
            Category = "explicit_comparison",
            UserMessage = "Compare Xavier's combat gear and augmentations with another typical Divison operative. What does Xavier have that's standard, and what might someone else have that's custom or specialized?",
            ExpectedSubject = "Xavier",
            ContaminationRisk = "N/A (comparison is allowed, but check for lore bleed into Xavier's section)",
            ProbePrompt = """
You are answering as the visible_response / ProseWriter component.
The user wants a comparison of Xavier's gear with another Division operative.
You may reference that some operatives have custom equipment (like prosthetic arms or specialized neural interfaces)
while keeping Xavier's description focused on his standard-issue gear.
Do NOT attribute any custom equipment to Xavier himself.
""",
        },
    ];
}

/// <summary>
/// A single probe turn in the live LLM lore consistency test.
/// </summary>
public sealed record LiveProbeTurn
{
    /// <summary>Turn number (1-based).</summary>
    [JsonPropertyName("turn_number")]
    public required int TurnNumber { get; init; }

    /// <summary>Category of probe: appearance, gear, body_tech, backstory, explicit_comparison.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>The user-facing message that triggers the roleplay response.</summary>
    [JsonPropertyName("user_message")]
    public required string UserMessage { get; init; }

    /// <summary>The expected active character/subject for this turn.</summary>
    [JsonPropertyName("expected_subject")]
    public required string ExpectedSubject { get; init; }

    /// <summary>Description of what contamination risk this probe targets.</summary>
    [JsonPropertyName("contamination_risk")]
    public required string ContaminationRisk { get; init; }

    /// <summary>System-like prompt that instructs the agent about lore boundaries for this turn.</summary>
    [JsonPropertyName("probe_prompt")]
    public required string ProbePrompt { get; init; }
}
