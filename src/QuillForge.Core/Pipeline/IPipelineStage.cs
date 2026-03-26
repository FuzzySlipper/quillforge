using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// A single stage in the forge pipeline. Each stage is independently testable.
/// </summary>
public interface IPipelineStage
{
    string StageName { get; }
    ForgeStage StageEnum { get; }
    IAsyncEnumerable<ForgeEvent> ExecuteAsync(ForgeContext context, CancellationToken ct);
}
