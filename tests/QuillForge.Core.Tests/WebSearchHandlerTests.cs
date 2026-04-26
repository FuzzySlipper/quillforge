using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public class WebSearchHandlerTests
{
    private static readonly AgentContext DefaultContext = new()
    {
        SessionId = Guid.CreateVersion7(),
        ActiveMode = Mode.Guide,
    };

    [Fact]
    public async Task ProviderNonRetryableFailure_ReturnsNonRetryableToolResult()
    {
        var service = new ThrowingWebSearchService(new WebSearchProviderException(
            "brave",
            "Brave Search returned HTTP 422. Do not retry this same web_search during the current tool loop.",
            statusCode: 422,
            canRetrySameRequest: false));
        var handler = new WebSearchHandler(service, NullLogger<WebSearchHandler>.Instance);
        using var doc = JsonDocument.Parse("""{"query":"oversized search"}""");
        var input = new ToolInput(doc.RootElement);

        var result = await handler.HandleAsync(input, DefaultContext);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("HTTP 422", result.Error!, StringComparison.Ordinal);
        Assert.Contains("Do not retry", result.Error!, StringComparison.Ordinal);
    }

    private sealed class ThrowingWebSearchService : IWebSearchService
    {
        private readonly WebSearchProviderException _exception;

        public ThrowingWebSearchService(WebSearchProviderException exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            throw _exception;
        }
    }
}
