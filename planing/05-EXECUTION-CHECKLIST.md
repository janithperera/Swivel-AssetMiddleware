# Execution Checklist — Build Order & Orchestration

## Recommended Build Order

Build in this exact order. Each step builds on the previous one. Do NOT jump ahead.

---

## Phase 0 — Scaffolding (30 min)

| # | Step | Command / Action | Done |
|---|------|------------------|------|
| 0.1 | Create solution folder structure | `mkdir -p src tests infra config wiremock/mappings wiremock/__files` | [ ] |
| 0.2 | Create .NET solution | `dotnet new sln -n AssetMiddleware -o src` | [ ] |
| 0.3 | Create Domain project | `dotnet new classlib -n AssetMiddleware.Domain -o src/AssetMiddleware.Domain` | [ ] |
| 0.4 | Create Application project | `dotnet new classlib -n AssetMiddleware.Application -o src/AssetMiddleware.Application` | [ ] |
| 0.5 | Create Infrastructure project | `dotnet new classlib -n AssetMiddleware.Infrastructure -o src/AssetMiddleware.Infrastructure` | [ ] |
| 0.6 | Create API/Host project | `dotnet new web -n AssetMiddleware.Api -o src/AssetMiddleware.Api` | [ ] |
| 0.7 | Create MockServer project (optional) | `dotnet new console -n AssetMiddleware.MockServer -o src/AssetMiddleware.MockServer` | [ ] |
| 0.8 | Create test projects | `dotnet new xunit -n AssetMiddleware.Domain.Tests -o tests/AssetMiddleware.Domain.Tests` | [ ] |
| 0.9 | Add all projects to solution | `dotnet sln src/AssetMiddleware.sln add src/**/*.csproj tests/**/*.csproj` | [ ] |
| 0.10 | Set target framework | Ensure all `.csproj` files target `net10.0` | [ ] |
| 0.11 | Add project references | See reference commands below | [ ] |
| 0.12 | Add NuGet packages | See package list below | [ ] |
| 0.13 | Create `.gitignore` | `dotnet new gitignore` | [ ] |
| 0.14 | Create `docker-compose.yml` | Service Bus Emulator + WireMock | [ ] |
| 0.15 | Create `appsettings.json` | All config sections with placeholders | [ ] |
| 0.16 | Create `appsettings.Development.json` | Emulator connection string + localhost:9090 | [ ] |
| 0.17 | Verify solution builds | `dotnet build src/AssetMiddleware.sln` | [ ] |

### Project Reference Commands

```bash
# Application → Domain
dotnet add src/AssetMiddleware.Application reference src/AssetMiddleware.Domain

# Infrastructure → Application + Domain
dotnet add src/AssetMiddleware.Infrastructure reference src/AssetMiddleware.Application
dotnet add src/AssetMiddleware.Infrastructure reference src/AssetMiddleware.Domain

# Api → Application + Infrastructure + Domain
dotnet add src/AssetMiddleware.Api reference src/AssetMiddleware.Application
dotnet add src/AssetMiddleware.Api reference src/AssetMiddleware.Infrastructure
dotnet add src/AssetMiddleware.Api reference src/AssetMiddleware.Domain

# Tests → corresponding source projects
dotnet add tests/AssetMiddleware.Domain.Tests reference src/AssetMiddleware.Domain
dotnet add tests/AssetMiddleware.Application.Tests reference src/AssetMiddleware.Application
dotnet add tests/AssetMiddleware.Application.Tests reference src/AssetMiddleware.Domain
dotnet add tests/AssetMiddleware.Infrastructure.Tests reference src/AssetMiddleware.Infrastructure
dotnet add tests/AssetMiddleware.Infrastructure.Tests reference src/AssetMiddleware.Application
dotnet add tests/AssetMiddleware.Infrastructure.Tests reference src/AssetMiddleware.Domain
```

### NuGet Packages

```bash
# Domain — no external packages (pure .NET)

# Application — no external packages (pure interfaces + logic)

# Infrastructure
dotnet add src/AssetMiddleware.Infrastructure package Azure.Messaging.ServiceBus
dotnet add src/AssetMiddleware.Infrastructure package Azure.Identity
dotnet add src/AssetMiddleware.Infrastructure package Microsoft.Extensions.Http.Resilience
dotnet add src/AssetMiddleware.Infrastructure package Microsoft.Extensions.Hosting.Abstractions
dotnet add src/AssetMiddleware.Infrastructure package Microsoft.Extensions.Http

# Api (host)
dotnet add src/AssetMiddleware.Api package Microsoft.Extensions.Hosting

# MockServer
dotnet add src/AssetMiddleware.MockServer package WireMock.Net

# Tests
dotnet add tests/AssetMiddleware.Domain.Tests package FluentAssertions
dotnet add tests/AssetMiddleware.Domain.Tests package NSubstitute
dotnet add tests/AssetMiddleware.Application.Tests package FluentAssertions
dotnet add tests/AssetMiddleware.Application.Tests package NSubstitute
dotnet add tests/AssetMiddleware.Infrastructure.Tests package FluentAssertions
dotnet add tests/AssetMiddleware.Infrastructure.Tests package NSubstitute
dotnet add tests/AssetMiddleware.Infrastructure.Tests package WireMock.Net
```

---

## Phase 1 — Domain Layer (Task 3 core) (45 min)

Build this first — no dependencies, fully testable immediately.

| # | Step | File(s) | Done |
|---|------|---------|------|
| 1.1 | Create event models | `Domain/Models/Events/*.cs` | [ ] |
| 1.2 | Create AssetHub request/response DTOs | `Domain/Models/AssetHub/*.cs` | [ ] |
| 1.3 | Create `EventTypes` constants | `Domain/Constants/EventTypes.cs` | [ ] |
| 1.4 | Create `ValidationError` + `ValidationResult` | `Domain/Validation/*.cs` | [ ] |
| 1.5 | Create domain exceptions | `Domain/Exceptions/*.cs` | [ ] |
| 1.6 | Implement `AssetIdGenerator` | `Domain/Rules/AssetIdGenerator.cs` | [ ] |
| 1.7 | Implement `OnsiteDerivation` | `Domain/Rules/OnsiteDerivation.cs` | [ ] |
| 1.8 | Implement `AssetTransformer` | `Domain/Transformers/AssetTransformer.cs` | [ ] |
| 1.9 | Write `AssetIdGenerator` unit tests | `tests/.../AssetIdGeneratorTests.cs` | [ ] |
| 1.10 | Write `OnsiteDerivation` unit tests | `tests/.../OnsiteDerivationTests.cs` | [ ] |
| 1.11 | Write `TransformRegistration` unit tests | `tests/.../RegistrationTransformerTests.cs` | [ ] |
| 1.12 | Write `TransformCheckIn` unit tests | `tests/.../CheckInTransformerTests.cs` | [ ] |
| 1.13 | Run all domain tests — must pass | `dotnet test tests/AssetMiddleware.Domain.Tests` | [ ] |

---

## Phase 2 — Application Layer (Interfaces + Handlers) (30 min)

| # | Step | File(s) | Done |
|---|------|---------|------|
| 2.1 | Define `IAssetHubClient` interface | `Application/Interfaces/IAssetHubClient.cs` | [ ] |
| 2.2 | Define `ITokenProvider` interface | `Application/Interfaces/ITokenProvider.cs` | [ ] |
| 2.3 | Define `IEventHandler<T>` interface | `Application/Interfaces/IEventHandler.cs` | [ ] |
| 2.4 | Define `IAssetTransformer` interface | `Application/Interfaces/IAssetTransformer.cs` | [ ] |
| 2.5 | Define `IDeadLetterQueueProcessor` interface | `Application/Interfaces/IDeadLetterQueueProcessor.cs` | [ ] |
| 2.6 | Define `IAssetStatusCache` interface | `Application/Interfaces/IAssetStatusCache.cs` | [ ] |
| 2.7 | Create configuration options classes | `Application/Configuration/*.cs` | [ ] |
| 2.8 | Implement `RegistrationEventHandler` | `Application/Handlers/RegistrationEventHandler.cs` | [ ] |
| 2.9 | Implement `CheckInEventHandler` | `Application/Handlers/CheckInEventHandler.cs` | [ ] |
| 2.10 | Implement `EventRouter` | `Application/Services/EventRouter.cs` | [ ] |
| 2.11 | Write handler unit tests (with mocked IAssetHubClient) | `tests/AssetMiddleware.Application.Tests/` | [ ] |
| 2.12 | Run tests | `dotnet test tests/AssetMiddleware.Application.Tests` | [ ] |

### Handler Implementation Notes

**`RegistrationEventHandler` flow:**
```
1. Transform event → CreateAssetRequest (Task 3 transformer)
2. Get Active status ID (from cache)
3. Dedup check: SearchAssetByIdAsync(projectId, assetId)
   → If match found: throw DuplicateAssetException ← stop here
   → If no match: continue
4. CreateAssetAsync(projectId, request)
5. If imageUrl present: download image → UploadPhotoAsync
6. Log success
```

**`CheckInEventHandler` flow:**
```
1. Transform event → (AssetId, UpdateAssetRequest) (Task 3 transformer)
2. Lookup asset: SearchAssetByIdAsync(projectId, assetId)
   → If no match: throw AssetNotFoundException ← can't update non-existent asset
   → If match: get the existing asset's `id`
3. UpdateAssetAsync(projectId, id, request)
4. Log success
```

---

## Phase 3 — Infrastructure Layer (HTTP Client + Service Bus) (90 min)

| # | Step | File(s) | Done |
|---|------|---------|------|
| 3.1 | Implement `OAuthTokenProvider` | `Infrastructure/Http/OAuthTokenProvider.cs` | [ ] |
| 3.2 | Implement `OAuthDelegatingHandler` | `Infrastructure/Http/OAuthDelegatingHandler.cs` | [ ] |
| 3.3 | Implement `AssetHubHttpClient` | `Infrastructure/Http/AssetHubHttpClient.cs` | [ ] |
| 3.4 | Implement `AssetStatusCache` | `Infrastructure/Caching/AssetStatusCache.cs` | [ ] |
| 3.5 | Configure resilience pipeline | `Infrastructure/Resilience/ResiliencePipelineConfigurator.cs` | [ ] |
| 3.6 | Implement `ServiceBusEventSubscriber` | `Infrastructure/ServiceBus/ServiceBusEventSubscriber.cs` | [ ] |
| 3.7 | Implement `DeadLetterQueueProcessor` | `Infrastructure/ServiceBus/DeadLetterQueueProcessor.cs` | [ ] |
| 3.8 | Create DI extension methods | `Infrastructure/DependencyInjection/*.cs` | [ ] |
| 3.9 | Optional: `RateLimitDelegatingHandler` | `Infrastructure/Http/RateLimitDelegatingHandler.cs` | [ ] |
| 3.10 | Optional: `CircuitBreakerStateTracker` | `Infrastructure/Resilience/CircuitBreakerStateTracker.cs` | [ ] |
| 3.11 | Set up WireMock stubs | `AssetMiddleware.MockServer/Program.cs` or test helpers | [ ] |
| 3.12 | Write integration tests with WireMock | `tests/AssetMiddleware.Infrastructure.Tests/` | [ ] |
| 3.13 | Run tests | `dotnet test tests/AssetMiddleware.Infrastructure.Tests` | [ ] |

---

## Phase 4 — API / Host Layer (Composition Root) (30 min)

| # | Step | File(s) | Done |
|---|------|---------|------|
| 4.1 | Wire up `Program.cs` (composition root) | `Api/Program.cs` | [ ] |
| 4.2 | Register all services via DI extensions | See example below | [ ] |
| 4.3 | Map DLQ replay endpoint | `Api/Endpoints/DlqReplayEndpoint.cs` | [ ] |
| 4.4 | Map status endpoint (optional) | `Api/Endpoints/StatusEndpoint.cs` | [ ] |
| 4.5 | Configure structured logging | `Program.cs` | [ ] |
| 4.6 | Create `Dockerfile` | `Api/Dockerfile` | [ ] |
| 4.7 | Add health check endpoint | `/health` | [ ] |
| 4.8 | Verify full application starts | `dotnet run --project src/AssetMiddleware.Api` | [ ] |

### Program.cs Skeleton

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────
builder.Services.Configure<ServiceBusOptions>(
    builder.Configuration.GetSection(ServiceBusOptions.SectionName));
builder.Services.Configure<AssetHubOptions>(
    builder.Configuration.GetSection(AssetHubOptions.SectionName));
builder.Services.Configure<ResilienceOptions>(
    builder.Configuration.GetSection(ResilienceOptions.SectionName));

// ── Domain / Application ──────────────────────────────────
builder.Services.AddSingleton<IAssetTransformer, AssetTransformer>();
builder.Services.AddScoped<IEventHandler<AssetRegistrationEvent>, RegistrationEventHandler>();
builder.Services.AddScoped<IEventHandler<AssetCheckInEvent>, CheckInEventHandler>();
builder.Services.AddSingleton<EventRouter>();

// ── Infrastructure ────────────────────────────────────────
builder.Services.AddAssetHubClient(builder.Configuration);
builder.Services.AddServiceBusSubscriber(builder.Configuration);
builder.Services.AddSingleton<IDeadLetterQueueProcessor, DeadLetterQueueProcessor>();

// ── Optional ──────────────────────────────────────────────
builder.Services.AddSingleton<CircuitBreakerStateTracker>();

var app = builder.Build();

// ── Endpoints ─────────────────────────────────────────────
app.MapDlqReplayEndpoints();
app.MapStatusEndpoints(); // optional
app.MapHealthChecks("/health");

app.Run();
```

---

## Phase 5 — Infrastructure as Code (Bicep) (30 min)

| # | Step | File(s) | Done |
|---|------|---------|------|
| 5.1 | Create `main.bicep` (orchestrator) | `infra/main.bicep` | [ ] |
| 5.2 | Create Service Bus module | `infra/modules/serviceBus.bicep` | [ ] |
| 5.3 | Create App Service module | `infra/modules/appService.bicep` | [ ] |
| 5.4 | Create Key Vault module | `infra/modules/keyVault.bicep` | [ ] |
| 5.5 | Create Managed Identity module | `infra/modules/managedIdentity.bicep` | [ ] |
| 5.6 | Create parameter files | `infra/parameters/dev.bicepparam` | [ ] |
| 5.7 | Validate Bicep compiles | `az bicep build --file infra/main.bicep` | [ ] |

### Bicep Resources to Provision

| Resource | Purpose |
|----------|---------|
| Service Bus Namespace + Topic + Subscription | Event ingestion |
| App Service Plan + Web App | Host the middleware |
| Key Vault | Store AssetHub client secret |
| Managed Identity | Authenticate to Service Bus + Key Vault without secrets |
| RBAC assignments | MI → Service Bus Receiver, MI → Key Vault Secrets Reader |

---

## Phase 6 — Docker Compose & Local Dev (20 min)

| # | Step | File(s) | Done |
|---|------|---------|------|
| 6.1 | Create `docker-compose.yml` | Root | [ ] |
| 6.2 | Create Service Bus Emulator config | `config/servicebus-emulator.config.json` | [ ] |
| 6.3 | Create WireMock mappings (optional, if using standalone WireMock Docker) | `wiremock/mappings/*.json` | [ ] |
| 6.4 | Test `docker compose up` | Verify emulator + WireMock start | [ ] |
| 6.5 | Test full pipeline end-to-end | Send message to emulator → verify WireMock receives API call | [ ] |

---

## Phase 7 — README & Polish (30 min)

| # | Step | Done |
|---|------|------|
| 7.1 | Write "How to run locally" section | [ ] |
| 7.2 | Write "Assumptions" section | [ ] |
| 7.3 | Write "Architecture decisions" section | [ ] |
| 7.4 | Write "What I would improve given more time" section | [ ] |
| 7.5 | Review all `appsettings.json` — no real secrets | [ ] |
| 7.6 | Verify `.gitignore` excludes `appsettings.Development.json` | [ ] |
| 7.7 | Run full test suite: `dotnet test` | [ ] |
| 7.8 | Run `dotnet build` — zero warnings | [ ] |
| 7.9 | Final code review against the "What we look for at senior level" table | [ ] |

---

## Final Self-Review Checklist

Cross-reference your implementation against the assessment rubric:

### Architecture
- [ ] Domain logic in Domain/Application layers — NOT in controllers or hosted services
- [ ] Application layer defines interfaces, Infrastructure implements them
- [ ] No business logic in `ServiceBusEventSubscriber` (it just routes)

### Dependency Injection
- [ ] Everything registered in DI container
- [ ] No `new()` on services anywhere
- [ ] `IOptions<T>` for all configuration
- [ ] `IHttpClientFactory` — never `new HttpClient()`

### Async Correctness
- [ ] `async`/`await` throughout — no `.Result` or `.Wait()`
- [ ] `CancellationToken` passed through every async method
- [ ] `ConfigureAwait(false)` in all library (non-API) projects

### Error Handling
- [ ] `ValidationException`, `DuplicateAssetException`, `AssetHubApiException` — specific types
- [ ] No `catch (Exception)` anywhere (except the Service Bus processor's outer catch)
- [ ] Structured logging with correlation IDs (`EventId` in scope)
- [ ] No swallowed exceptions

### Security
- [ ] No secrets in `appsettings.json` (only `appsettings.Development.json` for local dev)
- [ ] `appsettings.Development.json` in `.gitignore`
- [ ] Managed Identity in Bicep (not connection strings in prod)
- [ ] Key Vault for AssetHub client secret in prod

### Domain Rules
- [ ] Asset ID = `Make-Model-SerialNumber`, original casing
- [ ] Ownership = `"Subcontracted"` always, never from payload
- [ ] Dedup check before every create
- [ ] `projectId` from event used in all API paths
- [ ] Token refreshed proactively at 5000s (not reactively on 401)
- [ ] Onsite derivation: checkInDate null → throw, checkOutDate null → true, else → false

---

## Time Estimate Summary

| Phase | Estimated Time |
|-------|---------------|
| Phase 0 — Scaffolding | 30 min |
| Phase 1 — Domain Layer | 45 min |
| Phase 2 — Application Layer | 30 min |
| Phase 3 — Infrastructure Layer | 90 min |
| Phase 4 — API / Host Layer | 30 min |
| Phase 5 — Bicep IaC | 30 min |
| Phase 6 — Docker Compose | 20 min |
| Phase 7 — README & Polish | 30 min |
| **Total** | **~5 hours** |

Optional items (if time permits):
- Rate limiting handler: +15 min
- Status endpoint + circuit state tracker: +20 min
- Additional integration tests with WireMock: +30 min
