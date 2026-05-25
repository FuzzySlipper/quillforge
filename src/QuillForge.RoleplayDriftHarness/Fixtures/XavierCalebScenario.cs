using QuillForge.RoleplayDriftHarness.Models;

namespace QuillForge.RoleplayDriftHarness.Fixtures;

/// <summary>
/// Provides the synthetic Xavier/Caleb roleplay drift scenario.
///
/// Scenario overview:
/// - Xavier is an active character (Deepspace Hunter), the subject of roleplay.
/// - Caleb is an off-character whose personal details must not leak into Xavier's narrative.
/// - Forbidden details: prosthetic arm, Toring Chip (Caleb-specific).
/// - Shared body-tech evidence: standard neural interfaces, hunter augmentations (generic).
///
/// The scenario has two turns:
///   Turn 1: Ask about Xavier's appearance — should only return Xavier-specific facts.
///   Turn 2: Ask about Xavier's equipment/training — should not include Caleb-specific details.
///
/// Two versions are provided:
///   CleanScenario: all boundaries correctly exclude forbidden details (passes).
///   ContaminatedScenario: various boundaries leak forbidden details (fails with trace).
/// </summary>
public static class XavierCalebScenario
{
    /// <summary>
    /// A clean scenario where no forbidden details appear in any boundary output.
    /// This is the "passing" baseline test.
    /// </summary>
    public static RoleplayScenario CreateClean()
    {
        return new RoleplayScenario
        {
            Name = "xavier-caleb-clean",
            ActiveCharacter = "Xavier",
            OffCharacter = "Caleb",
            ForbiddenDetails =
            [
                "prosthetic arm",
                "Toring Chip",
            ],
            Turns =
            [
                new ScriptedTurn
                {
                    TurnNumber = 1,
                    UserMessage = "Describe Xavier's appearance for me.",
                    Boundaries =
                    [
                        // Turn 1: query_lore correctly returns Xavier lore only
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.QueryLore),
                            Component = "query_lore",
                            Content = "Xavier is a Deepspace Hunter with silver-streaked black hair and sharp grey eyes. " +
                                      "He wears standard Division-issue tactical gear, with recorded equipment limited to documented hunter tools.",
                            SourceRefs = ["characters/xavier.md"],
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "active_character",
                                AllowedUse = "inline",
                                LoreRefs = ["characters/xavier.md"],
                                SourceComponent = "query_lore",
                            },
                        },
                        // Turn 1: Narrative Director correctly focuses on Xavier
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.NarrativeDirector),
                            Component = "scene_brief",
                            Content = "Focus on Xavier's hunter appearance: silver-black hair, grey eyes, Division gear. " +
                                      "Keep description to known character facts. Do not reference other agents.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "active_character",
                                AllowedUse = "inline",
                                SourceComponent = "scene_brief",
                            },
                        },
                        // Turn 1: ProseWriter produces clean Xavier description
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.ProseWriter),
                            Component = "direct_scene",
                            Content = "Xavier stands with the quiet alertness of a seasoned Deepspace Hunter. " +
                                      "His silver-streaked black hair catches the pale light, and his sharp grey eyes scan the surroundings with practiced precision. " +
                                      "Standard Division tactical gear fits him well, showing signs of hard use.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "active_character",
                                AllowedUse = "inline",
                                SourceComponent = "direct_scene",
                            },
                        },
                        // Turn 1: Visible response — no forbidden details
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.VisibleResponse),
                            Component = "visible_response",
                            Content = "*Xavier meets your gaze with a cool, steady look. His silver-streaked black hair is slightly disheveled, " +
                                      "and there's a faint scar above his left eyebrow — a memento from his last Deepspace patrol.*",
                        },
                    ],
                },
                new ScriptedTurn
                {
                    TurnNumber = 2,
                    UserMessage = "Tell me more about Xavier's gear and augmentations.",
                    Boundaries =
                    [
                        // Turn 2: query_lore returns Xavier gear plus generic body-tech context without importing Caleb-specific facts.
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.QueryLore),
                            Component = "query_lore",
                            Content = "Xavier uses a standard-issue hunter carbine and carries a combat knife. " +
                                      "Shared Division records describe standard neural interface equipment common to hunter personnel. " +
                                      "Those shared records do not create a unique body-tech detail for Xavier.",
                            SourceRefs = ["characters/xavier.md", "world/body-tech.md"],
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = null,
                                Applicability = "shared_world",
                                AllowedUse = "context",
                                LoreRefs = ["characters/xavier.md", "world/body-tech.md"],
                                SourceComponent = "query_lore",
                            },
                        },
                        // Turn 2: Director uses shared body-tech context appropriately
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.NarrativeDirector),
                            Component = "scene_brief",
                            Content = "Describe Xavier's combat gear: standard Division carbine, combat knife, " +
                                      "neural interface (shared Division technology). Do not imply the neural interface is unique to Xavier.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "active_character",
                                AllowedUse = "inline",
                                SourceComponent = "scene_brief",
                            },
                        },
                        // Turn 2: ProseWriter produces clean gear description
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.ProseWriter),
                            Component = "direct_scene",
                            Content = "Xavier's Division-issue hunter carbine rests within easy reach, " +
                                      "its surface scarred from countless extractions. A combat knife is sheathed at his belt. " +
                                      "Like all Division operatives, he's equipped with a standard neural interface — " +
                                      "a common augmentation that enhances situational awareness in the field.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "active_character",
                                AllowedUse = "inline",
                                SourceComponent = "direct_scene",
                            },
                        },
                        // Turn 2: Visible response — generic body-tech is background, not Xavier-specific
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.VisibleResponse),
                            Component = "visible_response",
                            Content = "*Xavier unclips his carbine and holds it out for inspection. " +
                                      "\"Standard issue,\" he says with a slight shrug. \"Does the job.\" " +
                                      "You notice the combat knife at his belt, well-worn from use. " +
                                      "A faint blue shimmer at his temple hints at the Division-standard neural interface — " +
                                      "nothing unique, just the usual field augmentation.*",
                        },
                    ],
                },
            ],
            SharedBodyTechEvidence =
            [
                "Standard neural interface augmentation (common to Division personnel)",
                "Hunter-grade tactical gear (standard issue)",
            ],
        };
    }

    /// <summary>
    /// A contaminated scenario where forbidden Caleb details appear at specific
    /// boundaries. Used to test that the drift detector correctly identifies
    /// the first appearance boundary and classifies the origin.
    /// </summary>
    public static RoleplayScenario CreateContaminatedAtBoundary(string boundaryType)
    {
        var clean = CreateClean();

        // Build contaminated turns based on which boundary to inject
        var turns = new List<ScriptedTurn>();

        foreach (var turn in clean.Turns)
        {
            var boundaries = new List<ScriptedBoundaryOutput>();
            foreach (var b in turn.Boundaries)
            {
                if (string.Equals(b.Boundary, boundaryType, StringComparison.OrdinalIgnoreCase))
                {
                    // Inject forbidden facts into this boundary
                    boundaries.Add(b with
                    {
                        Content = b.Content + " " +
                                  "He has a prosthetic arm with advanced combat functionality. " +
                                  "His Toring Chip interfaces with the Division tactical network.",
                    });
                }
                else
                {
                    boundaries.Add(b);
                }
            }
            turns.Add(turn with { Boundaries = boundaries });
        }

        return clean with
        {
            Name = $"xavier-caleb-contaminated-{boundaryType.ToLowerInvariant()}",
            Turns = turns,
        };
    }
}
