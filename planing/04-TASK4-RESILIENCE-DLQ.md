# Task 4 — Resilience: Retry, Circuit Breaker + DLQ Processor

## Goal
Wrap outbound API calls with retry (exponential backoff + jitter) and circuit breaker policies. Build a dead-letter queue processor for replaying failed events. All thresholds configurable. Idempotent replay.

---

## Step-by-Step Implementation

### Step 4.1 — Define Resilience Configuration

**File:** `Application/Configuration/ResilienceOptions.cs`

```csharp
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
```

**`appsettings.json`** (partial):
```json
{
  "Resilience": {
    "Retry": {
      "MaxRetryAttempts": 3,
      "BaseDelaySeconds": 2,
      "MaxDelaySeconds": 30
    },
    "CircuitBreaker": {
      "FailureRatio": 0.5,
      "SamplingDurationSeconds": 30,
      "MinimumThroughput": 10,
      "BreakDurationSeconds": 60
    }
  }
}
```

---

### Step 4.2 — Choose Approach: Microsoft.Extensions.Http.Resilience (Preferred)

The assessment optional says: *"Use the .NET 10 built-in resilience pipeline (`Microsoft.Extensions.Http.Resilience`) rather than adding a third-party library"*

`Microsoft.Extensions.Http.Resilience` is built on **Polly v8** and integrates directly into the `IHttpClientFactory` pipeline. This is the preferred approach for senior-level .NET 10.

**NuGet:** `Microsoft.Extensions.Http.Resilience` (ships with .NET 10 SDK)

---

### Step 4.3 — Implement Resilience Pipeline Configuration

**File:** `Infrastructure/Resilience/ResiliencePipelineConfigurator.cs`

```csharp
public static class ResiliencePipelineConfigurator
{
    /// <summary>
    /// Adds retry + circuit breaker to the AssetHub typed HttpClient.
    /// Called during DI registration.
    /// </summary>
    public static IHttpClientBuilder AddAssetHubResilience(
        this IHttpClientBuilder builder, IConfiguration configuration)
    {
        var resilienceOptions = configuration
            .GetSection(ResilienceOptions.SectionName)
            .Get<ResilienceOptions>() ?? new ResilienceOptions();

        builder.AddResilienceHandler("AssetHub", (resiliencePipelineBuilder, context) =>
        {
            // ── Retry Policy ─────────────────────────────────────
            resiliencePipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = resilienceOptions.Retry.MaxRetryAttempts,

                // Exponential backoff with jitter
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(resilienceOptions.Retry.BaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(resilienceOptions.Retry.MaxDelaySeconds),
                UseJitter = true,

                // Only retry transient errors: 5xx, timeouts, 429
                ShouldHandle = args => ValueTask.FromResult(
                    ShouldRetry(args.Outcome)),

                OnRetry = args =>
                {
                    var logger = context.ServiceProvider
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");

                    logger.LogWarning(
                        "Retry attempt {AttemptNumber} after {Delay}ms. " +
                        "Status: {StatusCode}, Exception: {ExceptionType}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Result?.StatusCode,
                        args.Outcome.Exception?.GetType().Name);

                    return ValueTask.CompletedTask;
                }
            });

            // ── Circuit Breaker ──────────────────────────────────
            resiliencePipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = resilienceOptions.CircuitBreaker.FailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(
                    resilienceOptions.CircuitBreaker.SamplingDurationSeconds),
                MinimumThroughput = resilienceOptions.CircuitBreaker.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(
                    resilienceOptions.CircuitBreaker.BreakDurationSeconds),

                ShouldHandle = args => ValueTask.FromResult(
                    ShouldRetry(args.Outcome)),

                OnOpened = args =>
                {
                    var logger = context.ServiceProvider
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");

                    logger.LogError(
                        "Circuit breaker OPENED. Break duration: {BreakDuration}s. " +
                        "Failing service will not be called.",
                        args.BreakDuration.TotalSeconds);

                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    var logger = context.ServiceProvider
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");

                    logger.LogInformation("Circuit breaker CLOSED. Service recovered.");

                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    var logger = context.ServiceProvider
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("AssetHub.Resilience");

                    logger.LogInformation("Circuit breaker HALF-OPEN. Testing service...");

                    return ValueTask.CompletedTask;
                }
            });

            // ── Timeout (overall) ────────────────────────────────
            resiliencePipelineBuilder.AddTimeout(TimeSpan.FromSeconds(30));
        });

        return builder;
    }

    /// <summary>
    /// Determines if the result is a transient failure worth retrying.
    /// Only server errors (5xx), timeouts, and rate limiting (429).
    /// NOT client errors (4xx except 429).
    /// </summary>
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        // Exception → retry (timeout, connection failure)
        if (outcome.Exception is not null)
            return outcome.Exception is HttpRequestException or TaskCanceledException;

        var statusCode = outcome.Result?.StatusCode;
        if (statusCode is null) return false;

        return statusCode switch
        {
            // Server errors → retry
            >= HttpStatusCode.InternalServerError => true,
            // Rate limiting → retry
            HttpStatusCode.TooManyRequests => true,
            // Request timeout → retry
            HttpStatusCode.RequestTimeout => true,
            // All other errors (4xx client errors) → DO NOT retry
            _ => false
        };
    }
}
```

**Key decisions:**

| Decision | Why |
|----------|-----|
| `AddResilienceHandler` | .NET 10 built-in. Wraps the `HttpMessageHandler` pipeline. |
| `BackoffType.Exponential` + `UseJitter = true` | Industry standard. Prevents thundering herd. |
| Only retry 5xx + 429 + timeouts | Client errors (400, 404, 409) are NOT transient — don't retry them. |
| Circuit breaker after retry | Order matters: Retry → Circuit Breaker → Timeout. Retry inside the circuit. |
| Logging on `OnOpened` | "Log clearly when the circuit opens" — explicit requirement. |
| `MaxDelay` cap | Prevents exponential delays from growing unbounded. |

---

### Step 4.4 — Update DI Registration to Include Resilience

**File:** `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (updated)

```csharp
// Typed HTTP client for AssetHub API — WITH auth handler AND resilience
services.AddHttpClient<IAssetHubClient, AssetHubHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<AssetHubOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
})
.AddHttpMessageHandler<OAuthDelegatingHandler>()
.AddAssetHubResilience(configuration);  // ← Add resilience pipeline
```

**Handler pipeline order:**
```
Request → OAuthDelegatingHandler → Resilience (Retry → CB → Timeout) → HttpClientHandler → Network
```

---

### Step 4.5 — Dead-Letter Queue Processor Interface

**File:** `Application/Interfaces/IDeadLetterQueueProcessor.cs`

```csharp
public interface IDeadLetterQueueProcessor
{
    Task<int> ReplayDeadLettersAsync(CancellationToken ct);
}
```

---

### Step 4.6 — Implement DLQ Processor (Infrastructure Layer)

**File:** `Infrastructure/ServiceBus/DeadLetterQueueProcessor.cs`

```csharp
public sealed class DeadLetterQueueProcessor : IDeadLetterQueueProcessor
{
    private readonly ServiceBusClient _client;
    private readonly IOptions<ServiceBusOptions> _options;
    private readonly ILogger<DeadLetterQueueProcessor> _logger;

    public DeadLetterQueueProcessor(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<DeadLetterQueueProcessor> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ReplayDeadLettersAsync(CancellationToken ct)
    {
        var opts = _options.Value;

        // Dead-letter sub-queue path
        var dlqPath = EntityNameHelper.FormatDeadLetterPath(
            EntityNameHelper.FormatSubscriptionPath(
                opts.TopicName, opts.SubscriptionName));

        // Use receiver (not processor) — we pull messages explicitly
        await using var receiver = _client.CreateReceiver(
            opts.TopicName,
            opts.SubscriptionName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        // Sender to re-queue messages back to the main topic
        await using var sender = _client.CreateSender(opts.TopicName);

        var replayedCount = 0;

        _logger.LogInformation("Starting DLQ replay for {Topic}/{Subscription}",
            opts.TopicName, opts.SubscriptionName);

        while (!ct.IsCancellationRequested)
        {
            // Receive batch of messages (with timeout)
            var messages = await receiver.ReceiveMessagesAsync(
                maxMessages: 20,
                maxWaitTime: TimeSpan.FromSeconds(5),
                cancellationToken: ct).ConfigureAwait(false);

            if (messages.Count == 0)
            {
                _logger.LogInformation("No more dead-letter messages. Replayed {Count} total.", replayedCount);
                break;
            }

            foreach (var message in messages)
            {
                try
                {
                    // Create a new message with the same body
                    // Add a replay header to track replay count
                    var replayMessage = new ServiceBusMessage(message.Body)
                    {
                        ContentType = message.ContentType,
                        Subject = message.Subject,
                        CorrelationId = message.CorrelationId,
                        MessageId = message.MessageId // SAME ID — for idempotency
                    };

                    // Copy application properties
                    foreach (var prop in message.ApplicationProperties)
                    {
                        replayMessage.ApplicationProperties[prop.Key] = prop.Value;
                    }

                    // Track replay attempts
                    var replayCount = message.ApplicationProperties
                        .TryGetValue("ReplayCount", out var count) ? (int)count + 1 : 1;
                    replayMessage.ApplicationProperties["ReplayCount"] = replayCount;

                    _logger.LogInformation(
                        "Replaying dead-letter message {MessageId} (replay #{ReplayCount}). " +
                        "Original dead-letter reason: {Reason}",
                        message.MessageId, replayCount, message.DeadLetterReason);

                    // Send back to the main topic
                    await sender.SendMessageAsync(replayMessage, ct).ConfigureAwait(false);

                    // Complete the DLQ message (remove from dead-letter queue)
                    await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);

                    replayedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to replay dead-letter message {MessageId}. Abandoning.",
                        message.MessageId);

                    await receiver.AbandonMessageAsync(message, cancellationToken: ct)
                        .ConfigureAwait(false);
                }
            }
        }

        return replayedCount;
    }
}
```

**Key design decisions:**

| Decision | Why |
|----------|-----|
| `ServiceBusReceiver` (not processor) | Explicit pull model — we control when replay happens. |
| `SubQueue.DeadLetter` | Reads from the subscription's dead-letter sub-queue. |
| `PeekLock` mode | Safe — message stays locked until we complete/abandon. |
| Same `MessageId` on replay | **Idempotency** — if the message was already processed, Service Bus dedup prevents double processing (if dedup is enabled). Plus, the handler's dedup check (search AssetHub by ID) prevents duplicates. |
| `ReplayCount` application property | Track how many times a message has been replayed. |
| Batch receive with timeout | Efficient — pull 20 at a time, stop when empty. |
| `AbandonMessageAsync` on failure | Don't lose messages that fail to replay. |

**Idempotency guarantee (double-safe):**
1. **Service Bus level:** Same `MessageId` — if dedup is enabled on the topic, the bus rejects exact duplicates.
2. **Application level:** The `RegistrationEventHandler` calls `SearchAssetByIdAsync` before creating. If the asset already exists → `DuplicateAssetException` → dead-letter → no duplicate created.

---

### Step 4.7 — DLQ Replay Endpoint (API Layer)

**File:** `Api/Endpoints/DlqReplayEndpoint.cs`

```csharp
public static class DlqReplayEndpoint
{
    public static void MapDlqReplayEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dlq/replay", async (
            IDeadLetterQueueProcessor processor,
            CancellationToken ct) =>
        {
            var replayed = await processor.ReplayDeadLettersAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new { replayed, timestamp = DateTimeOffset.UtcNow });
        })
        .WithName("ReplayDeadLetters")
        .WithTags("DLQ")
        .Produces<object>(StatusCodes.Status200OK);
    }
}
```

---

### Step 4.8 — Status Endpoint (Optional)

**File:** `Infrastructure/Resilience/CircuitBreakerStateTracker.cs`

```csharp
public sealed class CircuitBreakerStateTracker
{
    private DateTimeOffset _lastSyncTime = DateTimeOffset.MinValue;
    private int _successCount;
    private int _failureCount;
    private string _circuitState = "Closed";

    public DateTimeOffset LastSyncTime => _lastSyncTime;
    public int TodaySuccessCount => _successCount;
    public int TodayFailureCount => _failureCount;
    public string CircuitState => _circuitState;

    public void RecordSuccess()
    {
        Interlocked.Increment(ref _successCount);
        _lastSyncTime = DateTimeOffset.UtcNow;
    }

    public void RecordFailure()
    {
        Interlocked.Increment(ref _failureCount);
    }

    public void SetCircuitState(string state)
    {
        _circuitState = state;
    }
}
```

**File:** `Api/Endpoints/StatusEndpoint.cs`

```csharp
public static class StatusEndpoint
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", (CircuitBreakerStateTracker tracker) =>
        {
            return Results.Ok(new
            {
                lastSyncTime = tracker.LastSyncTime,
                todaySuccessCount = tracker.TodaySuccessCount,
                todayFailureCount = tracker.TodayFailureCount,
                circuitState = tracker.CircuitState
            });
        })
        .WithName("GetStatus")
        .WithTags("Status")
        .Produces<object>(StatusCodes.Status200OK);
    }
}
```

---

### Step 4.9 — Wire Resilience into Circuit State Tracker

Update the resilience pipeline to report to the tracker:

```csharp
// Inside AddAssetHubResilience, in the circuit breaker config:
OnOpened = args =>
{
    var tracker = context.ServiceProvider.GetRequiredService<CircuitBreakerStateTracker>();
    tracker.SetCircuitState("Open");
    // ... existing logging
    return ValueTask.CompletedTask;
},
OnClosed = args =>
{
    var tracker = context.ServiceProvider.GetRequiredService<CircuitBreakerStateTracker>();
    tracker.SetCircuitState("Closed");
    // ... existing logging
    return ValueTask.CompletedTask;
},
OnHalfOpened = args =>
{
    var tracker = context.ServiceProvider.GetRequiredService<CircuitBreakerStateTracker>();
    tracker.SetCircuitState("HalfOpen");
    // ... existing logging
    return ValueTask.CompletedTask;
}
```

---

## Checklist for Task 4

- [ ] `ResilienceOptions` with `RetryOptions` + `CircuitBreakerOptions` — all configurable
- [ ] `appsettings.json` has all resilience thresholds
- [ ] Retry policy: exponential backoff with jitter
- [ ] Retry only on transient errors: 5xx, 429, timeouts. NOT on 4xx client errors
- [ ] Circuit breaker: opens on failure ratio, logs on state changes
- [ ] `Microsoft.Extensions.Http.Resilience` used (optional — preferred over raw Polly)
- [ ] Resilience pipeline added to `IHttpClientFactory` handler chain
- [ ] `DeadLetterQueueProcessor` reads from DLQ sub-queue
- [ ] Replayed messages use same `MessageId` for idempotency
- [ ] `ReplayCount` property tracks replay attempts
- [ ] DLQ processor handles failures per-message (abandon, don't crash)
- [ ] `POST /api/dlq/replay` endpoint exposed
- [ ] Replay is safe to run multiple times — dedup check prevents duplicates
- [ ] `CircuitBreakerStateTracker` (optional) — tracks state + counts
- [ ] `GET /api/status` endpoint (optional)
- [ ] `CancellationToken` passed through all async calls
- [ ] `ConfigureAwait(false)` in library code

---

## Common Pitfalls to Avoid

1. **Retrying on 400/404**: These are permanent failures. Don't waste retries.
2. **Circuit breaker BEFORE retry**: Wrong order. Retry should be inside the circuit breaker. Standard order: Retry → Circuit Breaker → Timeout.
3. **Forgetting jitter**: Without jitter, all retries hit the server at the same time (thundering herd).
4. **Hardcoded thresholds**: Must be configurable via `appsettings.json`.
5. **DLQ replay creating duplicates**: Must be idempotent. Dedup check in the handler prevents this.
6. **Not logging circuit state changes**: Explicit requirement — log when circuit opens.
7. **DLQ processor crashing on one bad message**: Handle per-message errors, don't let one bad message stop the whole batch.
