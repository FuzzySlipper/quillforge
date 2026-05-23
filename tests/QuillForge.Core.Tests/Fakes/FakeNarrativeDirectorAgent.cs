using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests.Fakes;

public sealed class FakeNarrativeDirectorAgent : NarrativeDirectorAgent
{
    private readonly Exception? _throwOnDirectScene;
    private readonly NarrativeDirectionResult? _directSceneResult;
    private readonly PlotGenerationResult? _plotResult;

    public FakeNarrativeDirectorAgent(
        Exception? throwOnDirectScene = null,
        NarrativeDirectionResult? directSceneResult = null,
        PlotGenerationResult? plotResult = null)
        : base(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!)
    {
        _throwOnDirectScene = throwOnDirectScene;
        _directSceneResult = directSceneResult;
        _plotResult = plotResult;
    }

    public override Task<NarrativeDirectionResult> DirectSceneAsync(
        NarrativeDirectionRequest request,
        AgentContext context,
        CancellationToken ct = default)
    {
        if (_throwOnDirectScene is not null)
        {
            throw _throwOnDirectScene;
        }

        return Task.FromResult(_directSceneResult ?? new NarrativeDirectionResult
        {
            ResponseText = "Fake scene response.",
            ToolRoundsUsed = 0,
        });
    }

    public override Task<PlotGenerationResult> GeneratePlotAsync(
        PlotGenerationRequest request,
        AgentContext context,
        CancellationToken ct = default)
    {
        if (_throwOnDirectScene is not null)
        {
            throw _throwOnDirectScene;
        }

        return Task.FromResult(_plotResult ?? new PlotGenerationResult
        {
            Markdown = "Fake plot.",
            ToolRoundsUsed = 0,
        });
    }
}
