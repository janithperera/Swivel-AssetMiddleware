namespace AssetMiddleware.Infrastructure.Resilience;

/// <summary>
/// Outermost handler in the AssetHub HTTP pipeline.
/// Records the final outcome of each call (after retries, circuit breaker, etc.)
/// into the <see cref="CircuitBreakerStateTracker"/> counters.
/// </summary>
public sealed class MetricsDelegatingHandler(CircuitBreakerStateTracker tracker) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                tracker.RecordSuccess();
            else
                tracker.RecordFailure();

            return response;
        }
        catch
        {
            tracker.RecordFailure();
            throw;
        }
    }
}
