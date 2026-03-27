using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// File-system backed character card store using YAML format.
/// Supports SillyTavern/TavernAI PNG card import.
/// </summary>
public sealed class FileSystemCharacterCardStore : ICharacterCardStore
{
    private readonly string _cardsPath;
    private readonly string _portraitsPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemCharacterCardStore> _logger;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public FileSystemCharacterCardStore(
        string cardsPath,
        string portraitsPath,
        AtomicFileWriter writer,
        ILogger<FileSystemCharacterCardStore> logger)
    {
        _cardsPath = cardsPath;
        _portraitsPath = portraitsPath;
        _writer = writer;
        _logger = logger;
    }

    public async Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
    {
        var path = Path.Combine(_cardsPath, fileName + ".yaml");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Character card not found: {Path}", path);
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(path, ct);
            var dto = YamlDeserializer.Deserialize<CharacterCardDto>(yaml);
            if (dto is null) return null;

            return new CharacterCard
            {
                Name = dto.Name ?? Path.GetFileNameWithoutExtension(path),
                Portrait = dto.Portrait,
                Personality = dto.Personality,
                Description = dto.Description,
                Scenario = dto.Scenario,
                Greeting = dto.Greeting,
                FileName = Path.GetFileNameWithoutExtension(path),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load character card: {Path}", path);
            return null;
        }
    }

    public async Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cardsPath);

        var dto = new CharacterCardDto
        {
            Name = card.Name,
            Portrait = card.Portrait,
            Personality = card.Personality,
            Description = card.Description,
            Scenario = card.Scenario,
            Greeting = card.Greeting,
        };

        var yaml = YamlSerializer.Serialize(dto);
        var path = Path.Combine(_cardsPath, fileName + ".yaml");
        await _writer.WriteAsync(path, yaml, ct);

        _logger.LogInformation("Saved character card: {FileName}", fileName);
    }

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_cardsPath))
        {
            return Task.FromResult<IReadOnlyList<CharacterCard>>([]);
        }

        var cards = Directory.GetFiles(_cardsPath, "*.yaml")
            .Select(f =>
            {
                try
                {
                    var yaml = File.ReadAllText(f);
                    var dto = YamlDeserializer.Deserialize<CharacterCardDto>(yaml);
                    return new CharacterCard
                    {
                        Name = dto?.Name ?? Path.GetFileNameWithoutExtension(f),
                        Portrait = dto?.Portrait,
                        Personality = dto?.Personality,
                        Description = dto?.Description,
                        Scenario = dto?.Scenario,
                        Greeting = dto?.Greeting,
                        FileName = Path.GetFileNameWithoutExtension(f),
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse card: {File}", f);
                    return new CharacterCard
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        FileName = Path.GetFileNameWithoutExtension(f),
                    };
                }
            })
            .OrderBy(c => c.Name)
            .ToList();

        return Task.FromResult<IReadOnlyList<CharacterCard>>(cards);
    }

    public string CardToPrompt(CharacterCard card)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Name:** {card.Name}");
        if (!string.IsNullOrWhiteSpace(card.Description))
            sb.AppendLine($"**Description:** {card.Description}");
        if (!string.IsNullOrWhiteSpace(card.Personality))
            sb.AppendLine($"**Personality:** {card.Personality}");
        if (!string.IsNullOrWhiteSpace(card.Scenario))
            sb.AppendLine($"**Scenario:** {card.Scenario}");
        return sb.ToString().TrimEnd();
    }

    public CharacterCard NewTemplate(string name = "New Character")
    {
        return new CharacterCard
        {
            Name = name,
            Portrait = null,
            Personality = "",
            Description = "",
            Scenario = "",
            Greeting = "",
            FileName = SanitizeFileName(name),
        };
    }

    public async Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Importing Tavern card from: {Path}", pngPath);

        var pngBytes = await File.ReadAllBytesAsync(pngPath, ct);
        var cardJson = ExtractTavernCardJson(pngBytes);

        if (cardJson is null)
            throw new InvalidOperationException("No character card data found in PNG tEXt chunks.");

        var card = ParseTavernJson(cardJson);

        // Copy portrait PNG
        var safeName = SanitizeFileName(card.Name);
        var portraitPath = Path.Combine(_portraitsPath, safeName + ".png");
        Directory.CreateDirectory(_portraitsPath);
        await _writer.WriteBytesAsync(portraitPath, pngBytes, ct);

        // Update card with portrait reference and file name
        card = card with
        {
            Portrait = safeName + ".png",
            FileName = safeName,
        };

        // Save as YAML
        await SaveAsync(safeName, card, ct);

        _logger.LogInformation("Imported Tavern card: {Name} -> {FileName}", card.Name, safeName);
        return card;
    }

    /// <summary>
    /// Parses PNG chunks to find tEXt chunks with "ccv3" or "chara" keywords,
    /// then base64-decodes the value and returns the JSON string.
    /// </summary>
    private static string? ExtractTavernCardJson(byte[] pngBytes)
    {
        // PNG starts with 8-byte signature
        if (pngBytes.Length < 8)
            return null;

        var offset = 8; // skip PNG signature

        string? charaData = null;

        while (offset + 8 <= pngBytes.Length)
        {
            // 4-byte length (big-endian)
            var chunkLength = (int)BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(offset, 4));
            offset += 4;

            // 4-byte chunk type
            var chunkType = Encoding.ASCII.GetString(pngBytes, offset, 4);
            offset += 4;

            if (offset + chunkLength + 4 > pngBytes.Length)
                break; // malformed

            if (chunkType == "tEXt")
            {
                // tEXt chunk: keyword\0text
                var chunkData = pngBytes.AsSpan(offset, chunkLength);
                var nullIndex = chunkData.IndexOf((byte)0);
                if (nullIndex >= 0)
                {
                    var keyword = Encoding.Latin1.GetString(chunkData[..nullIndex]);
                    var textValue = Encoding.Latin1.GetString(chunkData[(nullIndex + 1)..]);

                    if (keyword is "ccv3" or "chara")
                    {
                        try
                        {
                            var jsonBytes = Convert.FromBase64String(textValue);
                            var json = Encoding.UTF8.GetString(jsonBytes);

                            // Prefer ccv3 over chara
                            if (keyword == "ccv3")
                                return json;

                            charaData ??= json;
                        }
                        catch (FormatException)
                        {
                            // Not valid base64, skip
                        }
                    }
                }
            }

            // Skip chunk data + 4-byte CRC
            offset += chunkLength + 4;
        }

        return charaData;
    }

    /// <summary>
    /// Parses Tavern card JSON (V1, V2, or V3 format) into a CharacterCard.
    /// </summary>
    private static CharacterCard ParseTavernJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // V2/V3: fields are nested under "data"
        var data = root.TryGetProperty("data", out var dataEl) ? dataEl : root;

        var name = GetStringProp(data, "name") ?? "Unknown";
        var personality = GetStringProp(data, "personality");
        var description = GetStringProp(data, "description");
        var scenario = GetStringProp(data, "scenario");
        var greeting = GetStringProp(data, "first_mes") ?? GetStringProp(data, "greeting");

        return new CharacterCard
        {
            Name = name,
            Personality = personality,
            Description = description,
            Scenario = scenario,
            Greeting = greeting,
        };
    }

    private static string? GetStringProp(JsonElement el, string name)
    {
        return el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        // Replace spaces with hyphens, collapse multiples, trim
        var result = sb.ToString().Trim();
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", "-");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"-{2,}", "-");
        return result.ToLowerInvariant();
    }

    /// <summary>
    /// Internal DTO for YAML serialization (excludes FileName which is derived from the file path).
    /// </summary>
    private sealed class CharacterCardDto
    {
        public string? Name { get; set; }
        public string? Portrait { get; set; }
        public string? Personality { get; set; }
        public string? Description { get; set; }
        public string? Scenario { get; set; }
        public string? Greeting { get; set; }
    }
}
