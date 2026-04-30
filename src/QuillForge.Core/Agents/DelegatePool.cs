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
    public float? Temperature { get; init; }
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
    private readonly Func<string, ProviderAliasResolution> _aliasResolver;
    private readonly ILogger<DelegatePool> _logger;

    public DelegatePool(Func<string, ICompletionService> serviceFactory, ILogger<DelegatePool> logger)
        : this(serviceFactory, alias => ProviderAliasResolution.Resolved(alias, alias), logger)
    {
    }

    public DelegatePool(
        Func<string, ICompletionService> serviceFactory,
        Func<string, ProviderAliasResolution> aliasResolver,
        ILogger<DelegatePool> logger)
    {
        _serviceFactory = serviceFactory;
        _aliasResolver = aliasResolver;
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

        if (!TryResolveProviderAliases(taskList, out var resolvedTasks, out var providerSetupError))
        {
            _logger.LogWarning("Delegate provider setup invalid: {Error}", providerSetupError);
            return CreateProviderSetupFailureResults(taskList, providerSetupError!);
        }

        // Single task — skip semaphore overhead
        if (resolvedTasks.Count == 1)
        {
            var result = await ExecuteOneAsync(resolvedTasks[0], ct);
            return new Dictionary<string, DelegateResult> { [result.Id] = result };
        }

        var semaphore = new SemaphoreSlim(Math.Min(maxConcurrency, resolvedTasks.Count));
        var results = new Dictionary<string, DelegateResult>();
        var resultLock = new Lock();

        var running = resolvedTasks.Select(async task =>
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
    public async Task<DelegateResult> RunSingleAsync(DelegateTask task, CancellationToken ct = default)
    {
        var results = await RunAsync([task], maxConcurrency: 1, ct);
        return results[task.Id];
    }

    private bool TryResolveProviderAliases(
        IReadOnlyList<DelegateTask> tasks,
        out List<DelegateTask> resolvedTasks,
        out string? error)
    {
        var resolutions = new Dictionary<string, ProviderAliasResolution>(StringComparer.OrdinalIgnoreCase);
        var failureMessages = new List<string>();

        foreach (var task in tasks)
        {
            if (resolutions.ContainsKey(task.ProviderAlias))
            {
                continue;
            }

            ProviderAliasResolution resolution;
            try
            {
                resolution = _aliasResolver(task.ProviderAlias);
            }
            catch (Exception ex)
            {
                resolution = ProviderAliasResolution.Failed(task.ProviderAlias, ex.Message);
            }

            resolutions[task.ProviderAlias] = resolution;
            if (!resolution.IsResolved)
            {
                failureMessages.Add(
                    resolution.Error
                    ?? $"Provider alias '{task.ProviderAlias}' did not resolve to a registered provider.");
            }
        }

        if (failureMessages.Count > 0)
        {
            resolvedTasks = [];
            error = $"Provider setup error: {string.Join(" ", failureMessages)} No delegated tasks were invoked.";
            return false;
        }

        resolvedTasks = new List<DelegateTask>(tasks.Count);
        foreach (var task in tasks)
        {
            var resolvedAlias = resolutions[task.ProviderAlias].ResolvedAlias!;
            if (!string.Equals(task.ProviderAlias, resolvedAlias, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Resolved delegate provider alias {RequestedAlias} to registered provider {ResolvedAlias}",
                    task.ProviderAlias,
                    resolvedAlias);
            }

            resolvedTasks.Add(task with { ProviderAlias = resolvedAlias });
        }

        error = null;
        return true;
    }

    private static IReadOnlyDictionary<string, DelegateResult> CreateProviderSetupFailureResults(
        IReadOnlyList<DelegateTask> tasks,
        string error)
    {
        var results = new Dictionary<string, DelegateResult>();
        foreach (var task in tasks)
        {
            results[task.Id] = new DelegateResult
            {
                Id = task.Id,
                Content = "",
                Model = task.ModelOverride ?? "default",
                ProviderAlias = task.ProviderAlias,
                Metadata = task.Metadata,
                Error = error,
            };
        }

        return results;
    }

    private async Task<DelegateResult> ExecuteOneAsync(DelegateTask task, CancellationToken ct)
    {
        var model = task.ModelOverride ?? "default";
        try
        {
            var service = _serviceFactory(task.ProviderAlias);
            var request = new CompletionRequest
            {
                ProviderAlias = task.ProviderAlias,
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
