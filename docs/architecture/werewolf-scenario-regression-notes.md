# Werewolf Scenario Regression Notes

Task #835 adds behavior-focused scenario tests for the baseline Werewolf module. The tests intentionally assert typed engine facts, visibility metadata, stage state, win outcomes, and event-journal determinism rather than narrator prose or copyrighted rulebook wording.

## Baseline scope covered

- Seeded role assignment is deterministic for a fixed participant list and command script.
- Private role reveal and private player-choice facts are visible only through `GameVisibilityProjector` for the intended participant.
- Hidden role-assignment authority facts remain hidden from player projections.
- Scripted full-game flows cover village win, werewolf parity win, tied votes, all-abstain votes, missing votes, invalid votes, and final game-end state.
- Replay-style determinism is covered by comparing typed event signatures and role maps for identical seeds and scripted choices.

## Intentional abstractions

The v1 module models baseline social-deduction behavior without copying published rulebook prose, proprietary role text, or a specific commercial product's full night-order script. Tests use generic role names, typed events, and behavior assertions.

The `one_night_compatible` setup flag and `werewolf-one-night-follow-up` prompt asset document the intended compatibility path. Center-card mechanics, published One Night role order, and specialty role interactions remain follow-up module work; scenario tests assert that this path is acknowledged without treating those variant mechanics as implemented authority.
