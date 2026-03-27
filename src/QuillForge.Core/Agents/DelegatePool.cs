using QuillForge.Core.Models;
using QuillForge.Core.Services;
using Microsoft.Extensions.Logging;

namespace QuillForge.Core.Agents;

/// <summary>
/// A single unit of work to delegate to a lightweight agent.
/// </summary>
public sealed record DelegateTask
{
    public required string Id { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public required string ProviderAlias { get; init; }
    public string? ModelOverride { get; init; }
    public float Temperature { get; init; } = 0.7f;
    public int MaxTokens { get; init; } = 1024;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result from a single delegated task.
/// </summary>
public sealed record DelegateResult
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string Model { get; init; }
    public required string ProviderAlias { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Runs multiple lightweight agent tasks in parallel using registered providers.
/// Designed for simple prompt-in/text-out tasks (NPC decisions, council members, evaluations).
/// </summary>
public sealed class DelegatePool
{
    private readonly Func<string, ICompletionService> _serviceFactory;
    private readonly ILogger<DelegatePool> _logger;

    public DelegatePool(Func<string, ICompletionService> serviceFactory, ILogger<DelegatePool> logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    /// <summary>
    /// Execute all tasks in parallel with bounded concurrency. Returns results keyed by task ID.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DelegateResult>> RunAsync(
        IEnumerable<DelegateTask> tasks,
        int maxConcurrency = 8,
        CancellationToken ct = default)
    {
        var taskList = tasks.ToList();
        if (taskList.Count == 0)
            return new Dictionary<string, DelegateResult>();

        // Single task — skip semaphore overhead
        if (taskList.Count == 1)
        {
            var result = await ExecuteOneAsync(taskList[0], ct);
            return new Dictionary<string, DelegateResult> { [result.Id] = result };
        }

        var semaphore = new SemaphoreSlim(Math.Min(maxConcurrency, taskList.Count));
        var results = new Dictionary<string, DelegateResult>();
        var resultLock = new Lock();

        var running = taskList.Select(async task =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await ExecuteOneAsync(task, ct);
                lock (resultLock)
                {
                    results[result.Id] = result;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(running);
        return results;
    }

    /// <summary>
    /// Execute a single task synchronously (convenience wrapper).
    /// </summary>
    public Task<DelegateResult> RunSingleAsync(DelegateTask task, CancellationToken ct = default)
        => ExecuteOneAsync(task, ct);

    private async Task<DelegateResult> ExecuteOneAsync(DelegateTask task, CancellationToken ct)
    {
        var model = task.ModelOverride ?? "default";
        try
        {
            var service = _serviceFactory(task.ProviderAlias);
            var request = new CompletionRequest
            {
                Model = model,
                MaxTokens = task.MaxTokens,
                Temperature = task.Temperature,
                SystemPrompt = task.SystemPrompt,
                Messages =
                [
                    new CompletionMessage("user", new MessageContent(task.UserPrompt)),
                ],
            };

            var response = await service.CompleteAsync(request, ct);
            var text = response.Content.GetText();

            _logger.LogDebug(
                "Delegate task {TaskId} completed: {Tokens} tokens",
                task.Id, response.Usage.TotalTokens);

            return new DelegateResult
            {
                Id = task.Id,
                Content = text,
                Model = model,
                ProviderAlias = task.ProviderAlias,
                Metadata = task.Metadata,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delegate task {TaskId} failed ({Provider})", task.Id, task.ProviderAlias);
            return new DelegateResult
            {
                Id = task.Id,
                Content = "",
                Model = model,
                ProviderAlias = task.ProviderAlias,
                Metadata = task.Metadata,
                Error = ex.Message,
            };
        }
    }
}
