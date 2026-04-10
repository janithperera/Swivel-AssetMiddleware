# Event Handler Orchestration — Registration & Check-In Flows

## Purpose
This document details the **orchestration logic** inside the event handlers. These sit in the Application layer and coordinate Domain logic (transformer) with Infrastructure (API client). This is the glue that ties Tasks 1–4 together.

---

## Registration Event Handler

**File:** `Application/Handlers/RegistrationEventHandler.cs`

### Flow Diagram

```
ServiceBusEventSubscriber
  → EventRouter (peek eventType, deserialize)
    → RegistrationEventHandler.HandleAsync()
        ├── 1. Transform event → CreateAssetRequest (AssetTransformer)
        ├── 2. Get Active status ID (IAssetStatusCache)
        ├── 3. Dedup check: SearchAssetByIdAsync(projectId, assetId)
        │     ├── Match found → throw DuplicateAssetException → STOP
        │     └── No match → continue
        ├── 4. CreateAssetAsync(projectId, request)
        ├── 5. If imageUrl present → download + UploadPhotoAsync
        └── 6. Log success
```

### Implementation

```csharp
public sealed class RegistrationEventHandler : IEventHandler<AssetRegistrationEvent>
{
    private readonly IAssetHubClient _assetHubClient;
    private readonly IAssetTransformer _transformer;
    private readonly IAssetStatusCache _statusCache;
    private readonly ILogger<RegistrationEventHandler> _logger;
    private readonly IHttpClientFactory _httpClientFactory; // For image download

    public RegistrationEventHandler(
        IAssetHubClient assetHubClient,
        IAssetTransformer transformer,
        IAssetStatusCache statusCache,
        ILogger<RegistrationEventHandler> logger,
        IHttpClientFactory httpClientFactory)
    {
        _assetHubClient = assetHubClient;
        _transformer = transformer;
        _statusCache = statusCache;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task HandleAsync(AssetRegistrationEvent @event, CancellationToken ct)
    {
        // 1. Get cached Active status ID
        var activeStatusId = await _statusCache.GetActiveStatusIdAsync(ct)
            .ConfigureAwait(false);

        // 2. Transform event → CreateAssetRequest
        var request = _transformer.TransformRegistration(@event, activeStatusId);
        _logger.LogInformation("Transformed registration event. AssetId: {AssetId}", request.AssetId);

        // 3. Dedup check — ALWAYS before create
        var existing = await _assetHubClient
            .SearchAssetByIdAsync(@event.ProjectId, request.AssetId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogError(
                "Duplicate asset detected. AssetId: {AssetId} already exists with ID: {ExistingId}",
                request.AssetId, existing.Id);
            throw new DuplicateAssetException(
                $"Asset '{request.AssetId}' already exists in project '{@event.ProjectId}'.");
        }

        // 4. Create asset
        var createResponse = await _assetHubClient
            .CreateAssetAsync(@event.ProjectId, request, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset created. AssetId: {AssetId}, Id: {Id}",
            createResponse.Data.AssetId, createResponse.Data.Id);

        // 5. Upload photo if imageUrl is present
        if (!string.IsNullOrWhiteSpace(@event.ImageUrl))
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient("ImageDownload");
                using var imageStream = await httpClient.GetStreamAsync(@event.ImageUrl, ct)
                    .ConfigureAwait(false);

                var fileName = Path.GetFileName(new Uri(@event.ImageUrl).AbsolutePath);

                await _assetHubClient.UploadPhotoAsync(
                    @event.ProjectId,
                    createResponse.Data.Id,
                    imageStream,
                    fileName,
                    ct).ConfigureAwait(false);

                _logger.LogInformation("Photo uploaded for asset {AssetId}", request.AssetId);
            }
            catch (Exception ex)
            {
                // Photo upload failure should NOT fail the whole registration
                _logger.LogWarning(ex,
                    "Failed to upload photo for asset {AssetId}. Continuing.",
                    request.AssetId);
            }
        }
    }
}
```

### Key Notes

- **Dedup check is mandatory** — always runs before `CreateAssetAsync`. This is a domain rule.
- **Photo upload failure is non-fatal** — catches and logs a warning. The asset was already created successfully. Don't throw and dead-letter.
- **Image download uses a separate named HttpClient** — `"ImageDownload"`. No auth headers needed for external image URLs.
- **Active status ID is cached** — `IAssetStatusCache` fetches once on first call, caches in memory.

---

## Check-In Event Handler

**File:** `Application/Handlers/CheckInEventHandler.cs`

### Flow Diagram

```
ServiceBusEventSubscriber
  → EventRouter (peek eventType, deserialize)
    → CheckInEventHandler.HandleAsync()
        ├── 1. Transform event → (AssetId, UpdateAssetRequest) 
        ├── 2. Lookup asset: SearchAssetByIdAsync(projectId, assetId)
        │     ├── No match → log warning + throw (can't update non-existent asset)
        │     └── Match found → get existing asset `id`
        ├── 3. UpdateAssetAsync(projectId, id, request)
        └── 4. Log success
```

### Implementation

```csharp
public sealed class CheckInEventHandler : IEventHandler<AssetCheckInEvent>
{
    private readonly IAssetHubClient _assetHubClient;
    private readonly IAssetTransformer _transformer;
    private readonly ILogger<CheckInEventHandler> _logger;

    public CheckInEventHandler(
        IAssetHubClient assetHubClient,
        IAssetTransformer transformer,
        ILogger<CheckInEventHandler> logger)
    {
        _assetHubClient = assetHubClient;
        _transformer = transformer;
        _logger = logger;
    }

    public async Task HandleAsync(AssetCheckInEvent @event, CancellationToken ct)
    {
        // 1. Transform event → (AssetId, UpdateAssetRequest)
        var (assetId, updateRequest) = _transformer.TransformCheckIn(@event);

        _logger.LogInformation(
            "Transformed check-in event. AssetId: {AssetId}, Onsite: {Onsite}",
            assetId, updateRequest.Onsite);

        // 2. Lookup existing asset to get its internal ID
        var existing = await _assetHubClient
            .SearchAssetByIdAsync(@event.ProjectId, assetId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _logger.LogError(
                "Asset not found for check-in update. AssetId: {AssetId}, Project: {ProjectId}",
                assetId, @event.ProjectId);
            throw new AssetHubApiException(
                $"Asset '{assetId}' not found in project '{@event.ProjectId}'. " +
                "Cannot update onsite status for non-existent asset.");
        }

        // 3. Update onsite status using the internal ID from the search result
        var response = await _assetHubClient
            .UpdateAssetAsync(@event.ProjectId, existing.Id, updateRequest, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset onsite status updated. Id: {Id}, Onsite: {Onsite}",
            response.Data.Id, response.Data.Onsite);
    }
}
```

### Key Notes

- **Must look up existing asset** — check-in events only have `make/model/serialNumber`, not the internal `id`. We generate the `AssetId` (e.g., `Caterpillar-320-SN-9901`), search for it, and use the returned `id` for the PATCH call.
- **Asset not found is an error** — you can't check in an asset that hasn't been registered. This should throw and dead-letter.
- **Onsite derivation is done by the transformer** — handler doesn't know the rules.
- **Check-in update is idempotent** — PATCHing `onsite: true` when it's already `true` is safe. This makes DLQ replay safe.

---

## Handler Registration in DI

```csharp
// Program.cs or DI extension method
services.AddScoped<IEventHandler<AssetRegistrationEvent>, RegistrationEventHandler>();
services.AddScoped<IEventHandler<AssetCheckInEvent>, CheckInEventHandler>();
services.AddSingleton<IAssetTransformer, AssetTransformer>();
```

**Scoped lifetime** for handlers because they may hold per-request state via injected services (e.g., `IAssetHubClient` which uses `HttpClient`). `AssetTransformer` is stateless → singleton is fine.

---

## Error Classification in Service Bus Subscriber

| Exception Type | Action | Rationale |
|---------------|--------|-----------|
| `ValidationException` | Dead-letter | Permanent — bad data won't fix itself on retry |
| `DuplicateAssetException` | Dead-letter | Permanent — asset exists, retry will find the same thing |
| `AssetHubApiException` (404 - not found) | Dead-letter | Permanent — asset doesn't exist for check-in |
| `AssetHubApiException` (5xx) | Abandon → retry | Transient — server may recover |
| `HttpRequestException` | Abandon → retry | Transient — network issue |
| `BrokenCircuitException` | Abandon → retry | Transient — circuit will close eventually |
| `TaskCanceledException` | Abandon → retry | Timeout — may succeed next time |
| Other `Exception` | Abandon → retry | Unknown — let bus retry to max delivery count |
