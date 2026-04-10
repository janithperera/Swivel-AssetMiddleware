namespace AssetMiddleware.Application.Configuration;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public RetryOptions Retry { get; init; } = new();
    public CircuitBreakerOptions CircuitBreaker { get; init; } = new();
}

public sealed class RetryOptions
{
    public int MaxRetryAttempts { get; init; } = 3;
    public int BaseDelaySeconds { get; init; } = 2;
    public int MaxDelaySeconds { get; init; } = 30;
}

public sealed class CircuitBreakerOptions
{
    public double FailureRatio { get; init; } = 0.5;
    public int SamplingDurationSeconds { get; init; } = 30;
    public int MinimumThroughput { get; init; } = 10;
    public int BreakDurationSeconds { get; init; } = 60;
}
