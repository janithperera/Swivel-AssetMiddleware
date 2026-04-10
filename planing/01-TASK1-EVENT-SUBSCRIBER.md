# Task 1 — Event Subscriber (Azure Service Bus)

## Goal
Build a `BackgroundService` that subscribes to Azure Service Bus, processes `asset.registration.submitted` and `asset.checkin.updated` events, routes them to the correct handler, and handles failures without crashing the host.

---

## Step-by-Step Implementation

### Step 1.1 — Define Event Models (Domain Layer)

**File:** `Domain/Models/Events/FieldOpsEventBase.cs`
```csharp
public abstract class FieldOpsEventBase
{
    public string EventType { get; init; } = default!;
    public string EventId { get; init; } = default!;
    public string ProjectId { get; init; } = default!;
    public string SiteRef { get; init; } = default!;
}
```

**File:** `Domain/Models/Events/AssetRegistrationEvent.cs`
```csharp
public sealed class AssetRegistrationEvent : FieldOpsEventBase
{
    public RegistrationFields Fields { get; init; } = default!;
    public string? ImageUrl { get; init; }
}

public sealed class RegistrationFields
{
    public string AssetName { get; init; } = default!;
    public string Make { get; init; } = default!;
    public string Model { get; init; } = default!;
    public string SerialNumber { get; init; } = default!;
    public string? YearMfg { get; init; }
    public string? Category { get; init; }
    public string? Type { get; init; }
    public string? RatePerHour { get; init; }
    public string? Supplier { get; init; }
}
```

**File:** `Domain/Models/Events/AssetCheckInEvent.cs`
```csharp
public sealed class AssetCheckInEvent : FieldOpsEventBase
{
    public string SerialNumber { get; init; } = default!;
    public string Make { get; init; } = default!;
    public string Model { get; init; } = default!;
    public DateTimeOffset? CheckInDate { get; init; }
    public DateTimeOffset? CheckOutDate { get; init; }
}
```

**Why separate models?** Each event type has a distinct shape. Polymorphic deserialization will be handled by the router, not the models.

---

### Step 1.2 — Define Event Type Constants

**File:** `Domain/Constants/EventTypes.cs`
```csharp
public static class EventTypes
{
    public const string AssetRegistration = "asset.registration.submitted";
    public const string AssetCheckIn = "asset.checkin.updated";
}
```

---

### Step 1.3 — Define IEventHandler Interface (Application Layer)

**File:** `Application/Interfaces/IEventHandler.cs`
```csharp
public interface IEventHandler<in TEvent> where TEvent : FieldOpsEventBase
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
```

---

### Step 1.4 — Define Configuration Options

**File:** `Application/Configuration/ServiceBusOptions.cs`
```csharp
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string FullyQualifiedNamespace { get; init; } = default!;
    public string TopicName { get; init; } = default!;
    public string SubscriptionName { get; init; } = default!;
    public int MaxConcurrentCalls { get; init; } = 5; // Configurable concurrency
}
```

**Note:** `MaxConcurrentCalls` satisfies the optional requirement for configurable concurrency.

---

### Step 1.5 — Build the Event Router (Application Layer)

**File:** `Application/Services/EventRouter.cs`

The router reads the raw JSON message, peeks at `eventType`, deserializes to the correct type, and dispatches to the right handler.

```csharp
public sealed class EventRouter
{
    private readonly IEventHandler<AssetRegistrationEvent> _registrationHandler;
    private readonly IEventHandler<AssetCheckInEvent> _checkInHandler;
    private readonly ILogger<EventRouter> _logger;

    public EventRouter(
        IEventHandler<AssetRegistrationEvent> registrationHandler,
        IEventHandler<AssetCheckInEvent> checkInHandler,
        ILogger<EventRouter> logger)
    {
        _registrationHandler = registrationHandler;
        _checkInHandler = checkInHandler;
        _logger = logger;
    }

    public async Task RouteAsync(BinaryData messageBody, CancellationToken ct)
    {
        // 1. Peek at eventType using JsonDocument (no full deserialize yet)
        using var doc = JsonDocument.Parse(messageBody);
        var eventType = doc.RootElement.GetProperty("eventType").GetString();
        var eventId = doc.RootElement.GetProperty("eventId").GetString();

        // 2. BeginScope for correlation (fulfils optional requirement)
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = eventId,
            ["EventType"] = eventType
        }))
        {
            _logger.LogInformation("Received event {EventType} with ID {EventId}", eventType, eventId);

            switch (eventType)
            {
                case EventTypes.AssetRegistration:
                    var regEvent = messageBody.ToObjectFromJson<AssetRegistrationEvent>();
                    await _registrationHandler.HandleAsync(regEvent, ct).ConfigureAwait(false);
                    break;

                case EventTypes.AssetCheckIn:
                    var checkInEvent = messageBody.ToObjectFromJson<AssetCheckInEvent>();
                    await _checkInHandler.HandleAsync(checkInEvent, ct).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning("Unknown event type: {EventType}. Completing message.", eventType);
                    break;
            }

            _logger.LogInformation("Successfully processed event {EventId}", eventId);
        }
    }
}
```

**Key decisions:**
- `JsonDocument.Parse` for peeking is efficient — no allocations for full object.
- `BeginScope` adds `EventId` to ALL downstream log entries — satisfies correlation requirement.
- Unknown event types log a warning but don't throw — message is completed, not dead-lettered.

---

### Step 1.6 — Build the Service Bus Subscriber (Infrastructure Layer)

**File:** `Infrastructure/ServiceBus/ServiceBusEventSubscriber.cs`

```csharp
public sealed class ServiceBusEventSubscriber : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly EventRouter _router;
    private readonly IOptions<ServiceBusOptions> _options;
    private readonly ILogger<ServiceBusEventSubscriber> _logger;
    private ServiceBusProcessor? _processor;

    public ServiceBusEventSubscriber(
        ServiceBusClient client,
        EventRouter router,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusEventSubscriber> logger)
    {
        _client = client;
        _router = router;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        _processor = _client.CreateProcessor(
            opts.TopicName,
            opts.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = opts.MaxConcurrentCalls,
                AutoCompleteMessages = false // Manual complete/abandon
            });

        _processor.ProcessMessageAsync += async args =>
        {
            try
            {
                await _router.RouteAsync(args.Message.Body, args.CancellationToken)
                    .ConfigureAwait(false);

                // Success → complete the message (removes from queue)
                await args.CompleteMessageAsync(args.Message, args.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ValidationException ex)
            {
                // Domain validation failure → dead-letter immediately (no retry)
                _logger.LogError(ex, "Validation failed for message {MessageId}. Dead-lettering.",
                    args.Message.MessageId);

                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "ValidationFailed",
                    deadLetterErrorDescription: ex.Message,
                    cancellationToken: args.CancellationToken).ConfigureAwait(false);
            }
            catch (DuplicateAssetException ex)
            {
                // Duplicate → dead-letter (no point retrying)
                _logger.LogWarning(ex, "Duplicate asset detected for message {MessageId}. Dead-lettering.",
                    args.Message.MessageId);

                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "DuplicateAsset",
                    deadLetterErrorDescription: ex.Message,
                    cancellationToken: args.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Transient/unknown failure → abandon (bus will retry per its retry policy)
                _logger.LogError(ex, "Failed to process message {MessageId}. Abandoning for retry.",
                    args.Message.MessageId);

                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
                    .ConfigureAwait(false);
            }
        };

        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception,
                "Service Bus processor error. Source: {ErrorSource}, Entity: {EntityPath}",
                args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await _processor.DisposeAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

**Critical design decisions:**

| Decision | Why |
|----------|-----|
| `AutoCompleteMessages = false` | We need manual control: Complete on success, Abandon on transient failure, DeadLetter on permanent failure. |
| Specific `catch` blocks | Validation/duplicate → dead-letter (no retry). Other exceptions → abandon (bus retries). Senior-level error handling. |
| `AbandonMessageAsync` on transient errors | Message goes back to the queue. Service Bus retries per its `MaxDeliveryCount` setting. After max retries → auto dead-letter. |
| `CancellationToken` threaded through | Async correctness requirement. |
| `ConfigureAwait(false)` everywhere | Library code — no sync context needed. |

---

### Step 1.7 — DI Registration

**File:** `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (partial)

```csharp
public static IServiceCollection AddServiceBusSubscriber(
    this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<ServiceBusOptions>(
        configuration.GetSection(ServiceBusOptions.SectionName));

    // ServiceBusClient — injected via DI
    // In prod: uses Managed Identity via DefaultAzureCredential
    // Locally: uses connection string from config
    services.AddSingleton(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
        if (!string.IsNullOrEmpty(opts.ConnectionString))
            return new ServiceBusClient(opts.ConnectionString);

        return new ServiceBusClient(
            opts.FullyQualifiedNamespace,
            new DefaultAzureCredential());
    });

    services.AddSingleton<EventRouter>();
    services.AddHostedService<ServiceBusEventSubscriber>();

    return services;
}
```

**Note:** Add a `ConnectionString` property to `ServiceBusOptions` for local dev with the emulator. In prod, use `FullyQualifiedNamespace` + Managed Identity.

---

### Step 1.8 — Service Bus Emulator Setup

**File:** `config/servicebus-emulator.config.json`
```json
{
  "UserConfig": {
    "Namespaces": [
      {
        "Name": "fieldops-ns",
        "Topics": [
          {
            "Name": "fieldops-events",
            "Subscriptions": [
              {
                "Name": "assethub-processor",
                "MaxDeliveryCount": 5
              }
            ]
          }
        ]
      }
    ]
  }
}
```

**`appsettings.Development.json`** (partial):
```json
{
  "ServiceBus": {
    "ConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "fieldops-events",
    "SubscriptionName": "assethub-processor",
    "MaxConcurrentCalls": 1
  }
}
```

---

## Checklist for Task 1

- [ ] Event models created with `System.Text.Json` serialization attributes
- [ ] `EventRouter` peeks at `eventType`, deserializes correctly, dispatches to handler
- [ ] `ServiceBusEventSubscriber` extends `BackgroundService`
- [ ] `AutoCompleteMessages = false` — manual complete/abandon/dead-letter
- [ ] Validation failures → dead-letter (no retry)
- [ ] Transient failures → abandon (bus retries)
- [ ] `ProcessErrorAsync` handler logs errors without crashing the host
- [ ] `MaxConcurrentCalls` configurable via `ServiceBusOptions`
- [ ] `EventId` in log scope for correlation
- [ ] `CancellationToken` passed through all async calls
- [ ] `ConfigureAwait(false)` in all library code
- [ ] All config from `IOptions<ServiceBusOptions>` — nothing hardcoded
- [ ] Service Bus Emulator config file created
- [ ] `appsettings.Development.json` has emulator connection string
- [ ] `docker-compose.yml` has Service Bus Emulator service

---

## Common Pitfalls to Avoid

1. **Don't use `Task.Run` inside the processor callback** — the Service Bus SDK manages concurrency via `MaxConcurrentCalls`.
2. **Don't catch `Exception` and silently complete** — that swallows errors and loses messages.
3. **Don't call `.Result` or `.Wait()`** — breaks async correctness.
4. **Don't hardcode topic/subscription names** — config only.
5. **Don't let `ProcessErrorAsync` throw** — it would crash the processor.
