using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Agents;

/// <summary>
/// Runs multiple ResearchAgent instances in parallel, each with its own multi-turn ToolLoop.
/// Like DelegatePool but for full tool-using agents rather than single-shot completions.
/// </summary>
public sealed class ResearchPool
{
    private readonly ResearchAgent _agent;
    private readonly int _maxConcurrency;
    private readonly ILogger<ResearchPool> _logger;

    public ResearchPool(ResearchAgent agent, AppConfig appConfig, ILogger<ResearchPool> logger)
    {
        _agent = agent;
        _maxConcurrency = appConfig.Agents.Research.MaxConcurrency;
        _logger = logger;
    }

    public async Task<ResearchPoolResult> RunAsync(
        string project,
        IReadOnlyList<ResearchTopic> topics,
        AgentContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ResearchPool starting: {Count} topics, project={Project}", topics.Count, project);

        ResearchAgentResult[] results;

        if (topics.Count == 1)
        {
            // Single topic — skip semaphore overhead
            var result = await _agent.ResearchAsync(
                topics[0].Topic, topics[0].Focus, project, context, ct);
            results = [result];
        }
        else
        {
            var concurrency = Math.Min(_maxConcurrency, topics.Count);
            using var semaphore = new SemaphoreSlim(concurrency);
            var resultLock = new Lock();
            var collected = new List<ResearchAgentResult>(topics.Count);

            var tasks = topics.Select(async topic =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var result = await _agent.ResearchAsync(
                        topic.Topic, topic.Focus, project, context, ct);

                    lock (resultLock)
                    {
                        collected.Add(result);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Research failed for topic \"{Topic}\"", topic.Topic);
                    lock (resultLock)
                    {
                        collected.Add(new ResearchAgentResult
                        {
                            Topic = topic.Topic,
                            Summary = "",
                            Sources = [],
                            FilePath = $"research/{project}/{ResearchAgent.Slugify(topic.Topic)}.md",
                            Error = ex.Message,
                        });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            results = [.. collected];
        }

        _logger.LogInformation(
            "ResearchPool completed: {Succeeded}/{Total} topics succeeded",
            results.Count(r => r.Error is null), results.Length);

        return new ResearchPoolResult
        {
            Project = project,
            Results = results,
        };
    }
}
