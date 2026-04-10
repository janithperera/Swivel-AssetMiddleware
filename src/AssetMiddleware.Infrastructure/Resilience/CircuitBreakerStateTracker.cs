namespace AssetMiddleware.Infrastructure.Resilience;

/// <summary>
/// Thread-safe in-memory tracker for circuit breaker state and call counts.
/// Registered as singleton — updated by the resilience pipeline callbacks.
/// </summary>
public sealed class CircuitBreakerStateTracker
{
    private volatile string _circuitState = "Closed";
    private DateTimeOffset _lastStateChangeAt = DateTimeOffset.UtcNow;
    private int _successCount;
    private int _failureCount;

    public string CircuitState => _circuitState;
    public DateTimeOffset LastStateChangeAt => _lastStateChangeAt;
    public int SuccessCount => _successCount;
    public int FailureCount => _failureCount;

    public void SetCircuitState(string state)
    {
        _circuitState = state;
        _lastStateChangeAt = DateTimeOffset.UtcNow;
    }

    public void RecordSuccess() =>
        Interlocked.Increment(ref _successCount);

    public void RecordFailure() =>
        Interlocked.Increment(ref _failureCount);
}
