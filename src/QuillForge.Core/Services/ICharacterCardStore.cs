using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface ICharacterCardStore
{
    Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default);
    Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default);
    Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default);
    string CardToPrompt(CharacterCard card);
    CharacterCard NewTemplate(string name = "New Character");
    Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default);
}
