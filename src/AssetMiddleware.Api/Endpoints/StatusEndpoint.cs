using AssetMiddleware.Infrastructure.Resilience;

namespace AssetMiddleware.Api.Endpoints;

public static class StatusEndpoint
{
    public static IEndpointRouteBuilder MapStatusEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (CircuitBreakerStateTracker tracker) => Results.Ok(new
        {
            Status = "Running",
            Timestamp = DateTimeOffset.UtcNow,
            CircuitBreaker = new
            {
                State = tracker.CircuitState,
                LastStateChangeAt = tracker.LastStateChangeAt,
                SuccessCount = tracker.SuccessCount,
                FailureCount = tracker.FailureCount
            }
        }))
        .WithName("GetStatus")
        .WithSummary("Get integration middleware status and circuit breaker state")
        .WithTags("Status")
        .Produces<object>(StatusCodes.Status200OK);

        return app;
    }
}
