# AssetMiddleware — FieldOps → AssetHub Integration

A .NET 10 integration middleware that bridges **FieldOps** (source, Azure Service Bus) and **AssetHub** (target, REST API). It subscribes to equipment events, transforms and validates them, authenticates with AssetHub via OAuth 2.0, and writes asset records — with resilience, dead-letter handling, and full Bicep IaC.

---

## Project Documentation

- [ASSUMPTIONS.md](ASSUMPTIONS.md) — All domain, technical, and integration assumptions
- [ARCHITECTURE-DECISIONS.md](ARCHITECTURE-DECISIONS.md) — Key architecture and design decisions
- [FUTURE-IMPROVEMENTS.md](FUTURE-IMPROVEMENTS.md) — What could be added or improved

### Initial Planning

The `planing/` folder contains the upfront design work produced before implementation began:

- [00-ARCHITECTURE-AND-SOLUTION-STRUCTURE.md](planing/00-ARCHITECTURE-AND-SOLUTION-STRUCTURE.md) — Overall architecture and project structure plan
- [01-TASK1-EVENT-SUBSCRIBER.md](planing/01-TASK1-EVENT-SUBSCRIBER.md) — Event subscriber design (Service Bus, hosted service, routing)
- [02-TASK2-ASSETHUB-API-CLIENT.md](planing/02-TASK2-ASSETHUB-API-CLIENT.md) — AssetHub HTTP client and OAuth design
- [03-TASK3-DATA-TRANSFORMER.md](planing/03-TASK3-DATA-TRANSFORMER.md) — Data transformation and validation design
- [04-TASK4-RESILIENCE-DLQ.md](planing/04-TASK4-RESILIENCE-DLQ.md) — Resilience pipeline and DLQ processor design
- [05-EXECUTION-CHECKLIST.md](planing/05-EXECUTION-CHECKLIST.md) — Implementation checklist and task tracking
- [06-HANDLER-ORCHESTRATION.md](planing/06-HANDLER-ORCHESTRATION.md) — Handler orchestration flow design

---

## Table of Contents

1. [Local Setup & Running](#1-local-setup--running)
2. [API Endpoints](#2-api-endpoints)
3. [Running Tests](#3-running-tests)
4. [Assumptions](#4-assumptions)
5. [Architecture Decisions](#5-architecture-decisions)
6. [Infrastructure as Code (Bicep)](#6-infrastructure-as-code-bicep)
7. [What I Would Add Given More Time](#7-what-i-would-add-given-more-time)

---

## 1. Local Setup & Running

See [HOW_TO_RUN_ON_LOCAL.md](HOW_TO_RUN_ON_LOCAL.md) for the full step-by-step guide.

> The guide is written for **Apple Silicon** (M1/M2/M3/M4) Macs and covers Docker Desktop/Rosetta setup, `appsettings.Development.json` configuration, starting dependencies, running the API, and sending test events.

---

## 2. API Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/health` | Health check |
| `GET` | `/api/status` | Circuit breaker state, success/failure counts, last state change |
| `POST` | `/api/dlq/replay` | Replay all dead-lettered messages back to the main topic |
| `GET` | `/scalar/v1` | Scalar OpenAPI UI (development only) |
| `GET` | `/openapi/v1.json` | OpenAPI spec (development only) |

---

## 3. Running Tests

```bash
dotnet test
```

The solution has 55 tests across three test projects:

| Project | Tests | Coverage |
|---|---|---|
| `AssetMiddleware.Domain.Tests` | 13 | `AssetIdGenerator`, `OnsiteDerivation`, validation rules |
| `AssetMiddleware.Application.Tests` | 28 | `AssetTransformer`, `RegistrationEventHandler`, `CheckInEventHandler` |
| `AssetMiddleware.Infrastructure.Tests` | 14 | `AssetHubHttpClient` + `OAuthTokenProvider` against WireMock |

Infrastructure tests spin up their own `WireMockServer` on a random port — no `docker compose` needed for `dotnet test`.

---

## 4. Assumptions

### Domain rules

- **Asset ID format** — `Make-Model-SerialNumber` using a hyphen separator, preserving original casing. Example: `Caterpillar-320-SN-9901`. The middleware generates this; it is not supplied by FieldOps.
- **Ownership** — always hardcoded to `"Subcontracted"`. This is a fixed business rule — it is never mapped from the event payload, regardless of what the event contains.
- **Dedup check** — every registration event triggers a search by Asset ID before any create. If a match is found, processing stops with a validation error logged. The check-in handler also searches by Asset ID to locate the internal record ID before patching.
- **ONSITE derivation** — `checkInDate` present + `checkOutDate` is `null` → `onsite = true`. `checkOutDate` non-null → `onsite = false`. `checkInDate` null → `ValidationException` (event is invalid and should not be processed).
- **Token refresh** — tokens expire at 5400 seconds. The middleware refreshes proactively at `expiresAt - TokenRefreshBufferSeconds` (default 400 seconds buffer = refresh at 5000 seconds). Concurrency is managed with a `SemaphoreSlim` so parallel requests do not trigger multiple simultaneous refresh calls.
- **projectId routing** — `projectId` from the event is used as the `{projectId}` path parameter in all AssetHub project-scoped API calls. No hardcoded project IDs.

### API behvaiour

- The `GET /asset-statuses` endpoint is assumed to always return an `"Active"` status. The status ID is fetched once on startup and cached (`IAssetStatusCache`). If no Active status is found, the middleware logs an error and cannot process registrations until the cache is refreshed.
- Photo upload (`POST /attachments`) is treated as non-fatal. If it fails, the error is logged but the event overall is considered successfully processed — the asset record exists.
- A 404 from `GET /assets/{id}` during a check-in event is treated as a terminal error (the asset was never registered), not a retryable transient fault.

### Infrastructure

- The Service Bus Emulator `SAS_KEY_VALUE` placeholder is intentional — the emulator does not validate the shared access key.
- The `config/servicebus-config.json` pre-declares the topic and subscription. Actual production namespaces are provisioned by the Bicep modules.

---

## 5. Architecture Decisions

### Clean Architecture layering

The solution is split into four production layers with a strict dependency rule (inner layers never reference outer layers):

```
Domain  ←  Application  ←  Infrastructure  ←  Api
```

| Layer | Responsibility |
|---|---|
| **Domain** | Core value objects, domain models, domain exceptions. No external dependencies. |
| **Application** | Use-case orchestration, interfaces (`IAssetHubClient`, `IEventHandler<T>`), transformers. References only Domain. |
| **Infrastructure** | HTTP client, OAuth provider, Service Bus subscriber, resilience pipeline, DLQ processor. Implements Application interfaces. |
| **Api** | ASP.NET Core host, DI wiring, minimal API endpoints, OpenAPI config. References all layers. |

Business logic (transformation, validation, dedup rules) lives in Application/Domain and is easy to unit-test with no infrastructure dependencies.

### `IServiceScopeFactory` in the hosted service

`ServiceBusSubscriber` is registered as a singleton (required for `IHostedService`). The handlers it calls (`RegistrationEventHandler`, `CheckInEventHandler`) are scoped. Rather than making them singleton (which would prevent per-request isolation), the subscriber creates a new `IServiceScope` per message — giving each message its own DI scope without lifetime conflicts.

### `IHttpClientFactory` — never `new HttpClient()`

All HTTP clients are created via `IHttpClientFactory`. This avoids socket exhaustion from premature `HttpClient` disposal and enables the `DelegatingHandler` pipeline (OAuth token, company header) to be composed cleanly via named clients.

### Proactive token refresh (not reactive on 401)

The `OAuthTokenProvider` calculates `expiresAt = created_at + expires_in` from the token response and schedules a refresh at `expiresAt - buffer`. This avoids a class of race conditions where a token expires mid-flight and causes spurious 401s on the hot path. A 401 still triggers a single recovery attempt (fetch new token, retry once) as a safety net.

### Collect-all-errors validation

`AssetTransformer` accumulates all validation failures before returning, rather than throwing on the first missing field. This makes test assertions precise and gives operators a complete picture of why an event was rejected.

### Resilience pipeline (Microsoft.Extensions.Http.Resilience)

The .NET 10 built-in resilience library (`Microsoft.Extensions.Http.Resilience`) is used instead of a third-party library. The pipeline is: **retry with exponential backoff + jitter** (transient faults only — 5xx, timeouts, 429) → **circuit breaker** (configurable failure ratio, sampling window, break duration). `CircuitBreakerStateTracker` is a singleton that tracks state changes and exposes them through `GET /api/status`.

---

## 6. Infrastructure as Code (Bicep)

All Azure resources are provisioned by the Bicep templates in `infra/`.

### Resource topology

```
infra/
├── main.bicep                     # Orchestrator — calls all modules
├── modules/
│   ├── managedIdentity.bicep      # User-assigned Managed Identity
│   ├── keyVault.bicep             # Key Vault (RBAC mode, soft delete)
│   ├── serviceBus.bicep           # Service Bus namespace + topic + subscription
│   └── appService.bicep           # App Service plan + web app + KV references
└── parameters/
    ├── dev.bicepparam
    └── prod.bicepparam
```

### What is provisioned

| Resource | Notes |
|---|---|
| User-assigned Managed Identity | Used by App Service for all Azure RBAC assignments |
| Key Vault | RBAC authorization mode; MI granted `Key Vault Secrets User` |
| Service Bus (Standard) | Topic with duplicate detection (10-min window); MI granted `Azure Service Bus Data Receiver` |
| App Service (Linux B2) | `DOTNETCORE|10.0`; Key Vault references for `ClientId` and `ClientSecret` |

### Deploy

```bash
# Validate
az bicep build --file infra/main.bicep

# Deploy to dev
az deployment sub create \
  --location australiaeast \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

---

## 7. What I Would Add Given More Time

### Observability
- **OpenTelemetry** tracing with correlation IDs propagated from the Service Bus message `ApplicationProperties` through to every outbound HTTP call and log record. Currently log correlation is per-event via `EventId` in the log scope, but distributed trace spans across Service Bus → API → AssetHub are not wired.
- **Structured metrics** — Prometheus/OTLP counters for messages processed, DLQ depth, circuit breaker trips, and token refresh rate.

### Resilience
- **DLQ replay scheduling** — currently replay is triggered manually via `POST /api/dlq/replay`. A `BackgroundService` on a configurable cron schedule (e.g. every 15 minutes) would make recovery automatic.
- **Poison-message isolation** — messages that fail after `MaxDeliveryCount` retries go to the DLQ. A replay count cap (`ReplayCount` on the dead-lettered message) guards against infinite replay loops, but a more robust approach would move truly-unprocessable messages to a separate storage location (e.g. Blob Storage) with an alert.

### Security
- **Managed Identity for Service Bus** in production (not connection string). The `ServiceBusClient` would be constructed with `DefaultAzureCredential` and the fully-qualified namespace, eliminating the shared access key entirely. The Bicep already provisions `Azure Service Bus Data Receiver` RBAC — the code path for that is wrapped behind `appsettings.Development.json` selecting the connection string approach locally.
- **Secret rotation** — Key Vault references in App Service auto-rotate on the next App Service restart. For zero-downtime rotation, a Key Vault Event Grid trigger could invoke a slot swap.

### Testing
- **Contract tests** — Pact or a similar consumer-driven contract testing tool to verify the WireMock stubs remain in sync with the actual AssetHub API as it evolves.
- **End-to-end test** — a Docker Compose–based test that publishes a real message to the Service Bus Emulator and asserts that WireMock received the expected `POST /v1/projects/{projectId}/assets` call, verifying the full pipeline.
- **Load/soak test** — verify the concurrency limits (`MaxConcurrentCalls`) and circuit breaker thresholds hold under load using NBomber or k6.

### Operational
- **Idempotency key** — store processed `eventId` values in a distributed cache (Redis) with a TTL, so that replayed events that already succeeded are skipped without needing a round-trip to AssetHub for the dedup check.
- **Health check enrichment** — extend `/health` to check Service Bus connectivity and AssetHub reachability, not just process health.
