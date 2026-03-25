using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Rolls dice using standard notation (2d6, 1d20+5, 4d6kh3, etc.).
/// Uses cryptographic RNG for fairness.
/// </summary>
public sealed partial class RollDiceHandler : IToolHandler
{
    private readonly ILogger<RollDiceHandler> _logger;

    public RollDiceHandler(ILogger<RollDiceHandler> logger)
    {
        _logger = logger;
    }

    public string Name => "roll_dice";

    public ToolDefinition Definition => new(Name,
        "Roll dice using standard notation. Examples: '2d6', '1d20+5', '4d6kh3' (keep highest 3).",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "notation": {
                        "type": "string",
                        "description": "Dice notation (e.g. '2d6', '1d20+5', '4d6kh3')"
                    }
                },
                "required": ["notation"]
            }
            """).RootElement);

    public Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var notation = input.GetProperty("notation").GetString() ?? "";
        _logger.LogDebug("RollDiceHandler: rolling {Notation}", notation);

        try
        {
            var result = Roll(notation);
            _logger.LogDebug("RollDiceHandler: {Notation} = {Total} (rolls: {Rolls})",
                notation, result.Total, string.Join(", ", result.Rolls));
            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result)));
        }
        catch (FormatException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Invalid dice notation: {ex.Message}"));
        }
    }

    internal static DiceResult Roll(string notation)
    {
        var match = DicePattern().Match(notation.Trim().ToLowerInvariant());
        if (!match.Success)
        {
            throw new FormatException($"Cannot parse '{notation}'. Use format like '2d6', '1d20+5', '4d6kh3'.");
        }

        var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value) : 1;
        var sides = int.Parse(match.Groups["sides"].Value);
        var keepHighest = match.Groups["keep"].Success ? int.Parse(match.Groups["keep"].Value) : (int?)null;
        var modifier = match.Groups["mod"].Success ? int.Parse(match.Groups["mod"].Value) : 0;

        if (count < 1 || count > 100) throw new FormatException("Dice count must be 1-100.");
        if (sides < 2 || sides > 1000) throw new FormatException("Sides must be 2-1000.");
        if (keepHighest is not null && (keepHighest < 1 || keepHighest > count))
            throw new FormatException($"Keep value must be 1-{count}.");

        var rolls = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            rolls.Add(RandomNumberGenerator.GetInt32(1, sides + 1));
        }

        var kept = keepHighest is not null
            ? rolls.OrderByDescending(r => r).Take(keepHighest.Value).ToList()
            : rolls;

        var total = kept.Sum() + modifier;

        return new DiceResult(notation, rolls, kept, modifier, total);
    }

    [GeneratedRegex(@"^(?<count>\d+)?d(?<sides>\d+)(?:kh(?<keep>\d+))?(?<mod>[+-]\d+)?$")]
    private static partial Regex DicePattern();
}

public sealed record DiceResult(
    string Notation,
    List<int> Rolls,
    List<int> Kept,
    int Modifier,
    int Total);
