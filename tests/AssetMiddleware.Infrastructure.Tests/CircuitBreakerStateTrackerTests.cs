using AssetMiddleware.Infrastructure.Resilience;
using FluentAssertions;

namespace AssetMiddleware.Infrastructure.Tests;

public class CircuitBreakerStateTrackerTests
{
    private readonly CircuitBreakerStateTracker _sut = new();

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsClosed()
    {
        _sut.CircuitState.Should().Be("Closed");
    }

    [Fact]
    public void InitialCounts_AreZero()
    {
        _sut.SuccessCount.Should().Be(0);
        _sut.FailureCount.Should().Be(0);
    }

    // ── SetCircuitState ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Open")]
    [InlineData("HalfOpen")]
    [InlineData("Closed")]
    public void SetCircuitState_UpdatesState(string state)
    {
        _sut.SetCircuitState(state);

        _sut.CircuitState.Should().Be(state);
    }

    [Fact]
    public void SetCircuitState_UpdatesLastStateChangeAt()
    {
        var before = DateTimeOffset.UtcNow;
        _sut.SetCircuitState("Open");
        var after = DateTimeOffset.UtcNow;

        _sut.LastStateChangeAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── RecordSuccess / RecordFailure ─────────────────────────────────────

    [Fact]
    public void RecordSuccess_IncrementsByOne()
    {
        _sut.RecordSuccess();
        _sut.RecordSuccess();
        _sut.RecordSuccess();

        _sut.SuccessCount.Should().Be(3);
    }

    [Fact]
    public void RecordFailure_IncrementsByOne()
    {
        _sut.RecordFailure();
        _sut.RecordFailure();

        _sut.FailureCount.Should().Be(2);
    }

    // ── Thread safety ─────────────────────────────────────────────────────

    [Fact]
    public void RecordSuccess_ConcurrentIncrements_AreThreadSafe()
    {
        const int iterations = 10_000;

        Parallel.For(0, iterations, _ => _sut.RecordSuccess());

        _sut.SuccessCount.Should().Be(iterations);
    }

    [Fact]
    public void RecordFailure_ConcurrentIncrements_AreThreadSafe()
    {
        const int iterations = 10_000;

        Parallel.For(0, iterations, _ => _sut.RecordFailure());

        _sut.FailureCount.Should().Be(iterations);
    }

    [Fact]
    public void SetCircuitState_ConcurrentWrites_DoNotThrow()
    {
        var states = new[] { "Open", "Closed", "HalfOpen" };

        var act = () => Parallel.For(0, 10_000, i =>
            _sut.SetCircuitState(states[i % states.Length]));

        act.Should().NotThrow();
        _sut.CircuitState.Should().BeOneOf("Open", "Closed", "HalfOpen");
    }

    // ── Mixed operations ──────────────────────────────────────────────────

    [Fact]
    public void SuccessAndFailureCountsAreIndependent()
    {
        _sut.RecordSuccess();
        _sut.RecordSuccess();
        _sut.RecordFailure();

        _sut.SuccessCount.Should().Be(2);
        _sut.FailureCount.Should().Be(1);
    }
}
