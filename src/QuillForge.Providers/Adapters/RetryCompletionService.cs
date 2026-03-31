using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Adapters;

/// <summary>
/// Decorator that adds retry-with-backoff to any ICompletionService.
/// Retries on transient errors: HTTP 429/502/503/504 and overloaded engine errors.
/// </summary>
public sealed class RetryCompletionService : ICompletionService
{
    private readonly ICompletionService _inner;
    private readonly ILogger _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.TooManyRequests,        // 429
        HttpStatusCode.BadGateway,             // 502
        HttpStatusCode.ServiceUnavailable,     // 503
        HttpStatusCode.GatewayTimeout,         // 504
    ];

    private static readonly string[] TransientMessagePatterns =
    [
        "overloaded",
        "rate limit",
        "too many requests",
        "capacity",
        "temporarily unavailable",
    ];

    public RetryCompletionService(
        ICompletionService inner,
        ILogger logger,
        int maxRetries = 3,
        TimeSpan? baseDelay = null)
    {
        _inner = inner;
        _logger = logger;
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _inner.CompleteAsync(request, ct);
            }
            catch (Exception ex) when (attempt < _maxRetries && !ct.IsCancellationRequested && IsTransient(ex))
            {
                var delay = GetDelay(attempt);
                _logger.LogWarning(
                    "Transient error on attempt {Attempt}/{Max}, retrying in {Delay}s: {Message}",
                    attempt + 1, _maxRetries, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, ct);
            }
        }
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // For streaming, retry only on connection-level failures (before any events are yielded).
        // Once events start flowing, we can't transparently retry without duplicating output.
        for (var attempt = 0; ; attempt++)
        {
            IAsyncEnumerator<StreamEvent>? enumerator = null;
            StreamEvent? firstEvent = null;
            bool gotFirst;

            try
            {
                enumerator = _inner.StreamAsync(request, ct).GetAsyncEnumerator(ct);
                gotFirst = await enumerator.MoveNextAsync();
                if (gotFirst) firstEvent = enumerator.Current;
            }
            catch (Exception ex) when (attempt < _maxRetries && !ct.IsCancellationRequested && IsTransient(ex))
            {
                if (enumerator is not null) await enumerator.DisposeAsync();
                var delay = GetDelay(attempt);
                _logger.LogWarning(
                    "Transient stream error on attempt {Attempt}/{Max}, retrying in {Delay}s: {Message}",
                    attempt + 1, _maxRetries, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, ct);
                continue;
            }

            // Connected and got first event (or stream was empty) — yield everything
            if (gotFirst && firstEvent is not null)
            {
                yield return firstEvent;
            }

            if (gotFirst)
            {
                while (await enumerator!.MoveNextAsync())
                {
                    yield return enumerator.Current;
                }
            }

            await enumerator!.DisposeAsync();
            yield break;
        }
    }

    private TimeSpan GetDelay(int attempt)
    {
        // Exponential backoff: 2s, 4s, 8s + up to 1s jitter
        var backoff = _baseDelay * Math.Pow(2, attempt);
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        return backoff + jitter;
    }

    internal static bool IsTransient(Exception ex)
    {
        // Check HttpRequestException status code (.NET 5+)
        if (ex is HttpRequestException httpEx && httpEx.StatusCode is not null)
        {
            return TransientStatusCodes.Contains(httpEx.StatusCode.Value);
        }

        // Check exception message for transient patterns
        var message = ex.Message;
        if (ex.InnerException is not null)
        {
            message = $"{message} {ex.InnerException.Message}";
        }

        return TransientMessagePatterns.Any(p =>
            message.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
