using AssetMiddleware.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net;

namespace AssetMiddleware.Infrastructure.Resilience;

public static class ResiliencePipelineConfigurator
{
    public static IHttpClientBuilder AddAssetHubResilience(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var opts = configuration
            .GetSection(ResilienceOptions.SectionName)
            .Get<ResilienceOptions>() ?? new ResilienceOptions();

        builder.AddResilienceHandler("AssetHub", (pipeline, context) =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = opts.Retry.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(opts.Retry.BaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(opts.Retry.MaxDelaySeconds),
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                OnRetry = args =>
                {
                    var logger = context.ServiceProvider
                        .GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");

                    logger.LogWarning(
                        "Retry attempt {Attempt} after {Delay}ms. Status: {Status}, Exception: {Ex}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Result?.StatusCode,
                        args.Outcome.Exception?.GetType().Name);

                    return ValueTask.CompletedTask;
                }
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = opts.CircuitBreaker.FailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreaker.SamplingDurationSeconds),
                MinimumThroughput = opts.CircuitBreaker.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreaker.BreakDurationSeconds),
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                OnOpened = args =>
                {
                    var sp = context.ServiceProvider;
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");
                    var tracker = sp.GetRequiredService<CircuitBreakerStateTracker>();

                    tracker.SetCircuitState("Open");

                    logger.LogError(
                        "Circuit breaker OPENED. Break duration: {Duration}s.",
                        args.BreakDuration.TotalSeconds);

                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    var sp = context.ServiceProvider;
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");
                    var tracker = sp.GetRequiredService<CircuitBreakerStateTracker>();

                    tracker.SetCircuitState("Closed");

                    logger.LogInformation("Circuit breaker CLOSED. Service recovered.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    var sp = context.ServiceProvider;
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");
                    var tracker = sp.GetRequiredService<CircuitBreakerStateTracker>();

                    tracker.SetCircuitState("HalfOpen");

                    logger.LogInformation("Circuit breaker HALF-OPEN. Testing service...");
                    return ValueTask.CompletedTask;
                }
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

        return builder;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
            return outcome.Exception is HttpRequestException or TaskCanceledException;

        return outcome.Result?.StatusCode switch
        {
            >= HttpStatusCode.InternalServerError => true,
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.RequestTimeout => true,
            _ => false
        };
    }
}
