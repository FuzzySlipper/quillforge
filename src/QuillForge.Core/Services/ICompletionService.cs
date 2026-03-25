using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// The single LLM abstraction. Provider-specific SDK types never cross this boundary.
/// Implemented in QuillForge.Providers.
/// </summary>
public interface ICompletionService
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default);
}
