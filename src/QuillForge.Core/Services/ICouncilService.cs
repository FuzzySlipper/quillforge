using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface ICouncilService
{
    Task<IReadOnlyList<CouncilMember>> LoadMembersAsync(CancellationToken ct = default);
    Task<CouncilResult> RunCouncilAsync(string query, CancellationToken ct = default);
    string FormatForOrchestrator(CouncilResult result);
}
