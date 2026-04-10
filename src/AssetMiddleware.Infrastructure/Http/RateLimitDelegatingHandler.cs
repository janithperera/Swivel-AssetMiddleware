using System.Net;
using Microsoft.Extensions.Logging;

namespace AssetMiddleware.Infrastructure.Http;

/// <summary>
/// Handles 429 Too Many Requests by waiting for the Retry-After period and retrying once.
/// This handler is optional and sits outside the resilience pipeline — it handles the
/// <em>first</em> 429 gracefully before the retry policy counts it as a failure.
/// </summary>
public sealed class RateLimitDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<RateLimitDelegatingHandler> _logger;

    public RateLimitDelegatingHandler(ILogger<RateLimitDelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(5);

            _logger.LogWarning(
                "Rate limited by AssetHub. Waiting {RetryAfterSeconds}s before retrying {Method} {Uri}.",
                retryAfter.TotalSeconds, request.Method, request.RequestUri);

            await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);

            response.Dispose();
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
