# Architecture & Solution Structure Plan

## Overview

This document defines the solution architecture, project structure, layering, and foundational decisions for the FieldOps → AssetHub integration middleware.

---

## Solution Name

`FieldOps.AssetHub.Integration` (or `AssetMiddleware`)

## Target Framework

- **.NET 10** (latest LTS as of 2026)
- C# 13

---

## Clean Architecture Layers

```
┌─────────────────────────────────────────────────────────┐
│                    API / Host Layer                     │
│  (ASP.NET Core Minimal API + Worker Service)            │
│  - Program.cs (composition root, DI wiring)             │
│  - DLQ replay endpoint                                  │
│  - Status/health endpoint                               │
│  - appsettings.json / appsettings.Development.json      │
├─────────────────────────────────────────────────────────┤
│                  Application Layer                      │
│  (Interfaces, handlers, orchestration)                  │
│  - IAssetHubClient (interface)                          │
│  - IEventHandler<T> pattern                             │
│  - ITokenProvider (interface)                           │
│  - IAssetTransformer (interface)                        │
│  - RegistrationEventHandler                             │
│  - CheckInEventHandler                                  │
├─────────────────────────────────────────────────────────┤
│                    Domain Layer                         │
│  (Models, business rules, validation, exceptions)       │
│  - Event models (source)                                │
│  - AssetHub request/response DTOs (target)              │
│  - AssetIdGenerator                                     │
│  - EventTransformer (pure mapping + validation)         │
│  - Domain exceptions (ValidationException, etc.)        │
│  - Business rules (ownership = "Subcontracted")         │
├─────────────────────────────────────────────────────────┤
│                Infrastructure Layer                     │
│  (External integrations, HTTP clients, Service Bus)     │
│  - AssetHubHttpClient (implements IAssetHubClient)      │
│  - OAuthTokenProvider (implements ITokenProvider)       │
│  - ServiceBusEventSubscriber (BackgroundService)        │
│  - DeadLetterQueueProcessor                             │
│  - Resilience policies (Polly / MS resilience)          │
│  - WireMock setup helpers                               │
├─────────────────────────────────────────────────────────┤
│                    Tests Layer                          │
│  - Unit tests (Domain, Application)                     │
│  - Integration tests (Infrastructure with WireMock)     │
└─────────────────────────────────────────────────────────┘
```

---

## Project Structure (Physical)

```
src/
├── AssetMiddleware.sln
│
├── AssetMiddleware.Domain/
│   ├── AssetMiddleware.Domain.csproj
│   ├── Models/
│   │   ├── Events/
│   │   │   ├── FieldOpsEvent.cs                 # Base event
│   │   │   ├── AssetRegistrationEvent.cs        # asset.registration.submitted
│   │   │   ├── AssetCheckInEvent.cs             # asset.checkin.updated
│   │   │   └── RegistrationFields.cs            # Nested fields object
│   │   ├── AssetHub/
│   │   │   ├── CreateAssetRequest.cs
│   │   │   ├── UpdateAssetRequest.cs
│   │   │   ├── AssetSearchResponse.cs
│   │   │   ├── CreateAssetResponse.cs
│   │   │   ├── UpdateAssetResponse.cs
│   │   │   ├── TokenResponse.cs
│   │   │   └── AssetStatusResponse.cs
│   │   └── OnsiteStatus.cs
│   ├── Rules/
│   │   ├── AssetIdGenerator.cs                  # Make-Model-SerialNumber 
│   │   └── OnsiteDerivation.cs                  # checkIn/checkOut → bool
│   ├── Validation/
│   │   ├── ValidationResult.cs
│   │   └── ValidationError.cs
│   └── Exceptions/
│       ├── ValidationException.cs
│       ├── DuplicateAssetException.cs
│       └── AssetHubApiException.cs
│
├── AssetMiddleware.Application/
│   ├── AssetMiddleware.Application.csproj
│   ├── Interfaces/
│   │   ├── IAssetHubClient.cs
│   │   ├── ITokenProvider.cs
│   │   ├── IEventHandler.cs
│   │   ├── IAssetTransformer.cs
│   │   ├── IDeadLetterQueueProcessor.cs
│   │   └── IAssetStatusCache.cs
│   ├── Handlers/
│   │   ├── RegistrationEventHandler.cs
│   │   └── CheckInEventHandler.cs
│   ├── Services/
│   │   └── EventRouter.cs                       # Routes eventType → handler
│   └── Configuration/
│       ├── ServiceBusOptions.cs
│       ├── AssetHubOptions.cs
│       └── ResilienceOptions.cs
│
├── AssetMiddleware.Infrastructure/
│   ├── AssetMiddleware.Infrastructure.csproj
│   ├── Http/
│   │   ├── AssetHubHttpClient.cs                # Typed HttpClient
│   │   ├── OAuthTokenProvider.cs                # Token lifecycle
│   │   ├── OAuthDelegatingHandler.cs            # Attaches Bearer + X-Company-Id
│   │   └── RateLimitDelegatingHandler.cs        # 429 handling (optional)
│   ├── ServiceBus/
│   │   ├── ServiceBusEventSubscriber.cs         # BackgroundService
│   │   └── DeadLetterQueueProcessor.cs
│   ├── Resilience/
│   │   ├── ResiliencePipelineConfigurator.cs     # Retry + CB config
│   │   └── CircuitBreakerStateTracker.cs        # Exposes state (optional)
│   ├── Caching/
│   │   └── AssetStatusCache.cs                  # Caches "Active" statusId
│   └── DependencyInjection/
│       └── InfrastructureServiceCollectionExtensions.cs
│
├── AssetMiddleware.Api/
│   ├── AssetMiddleware.Api.csproj               # Host project (executable)
│   ├── Program.cs                               # Composition root
│   ├── Endpoints/
│   │   ├── DlqReplayEndpoint.cs                 # POST /api/dlq/replay
│   │   └── StatusEndpoint.cs                    # GET /api/status (optional)
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Dockerfile
│   └── Properties/
│       └── launchSettings.json
│
├── AssetMiddleware.MockServer/                   # Optional separate project
│   ├── AssetMiddleware.MockServer.csproj
│   └── Program.cs                               # WireMock standalone runner
│
tests/
├── AssetMiddleware.Domain.Tests/
│   ├── AssetMiddleware.Domain.Tests.csproj
│   ├── AssetIdGeneratorTests.cs
│   ├── OnsiteDerivationTests.cs
│   └── TransformerTests/
│       ├── RegistrationTransformerTests.cs
│       └── CheckInTransformerTests.cs
│
├── AssetMiddleware.Application.Tests/
│   ├── AssetMiddleware.Application.Tests.csproj
│   ├── RegistrationEventHandlerTests.cs
│   └── CheckInEventHandlerTests.cs
│
└── AssetMiddleware.Infrastructure.Tests/
    ├── AssetMiddleware.Infrastructure.Tests.csproj
    ├── AssetHubHttpClientTests.cs               # WireMock integration
    ├── OAuthTokenProviderTests.cs
    └── DeadLetterQueueProcessorTests.cs

infra/
├── main.bicep
├── modules/
│   ├── serviceBus.bicep
│   ├── appService.bicep
│   ├── keyVault.bicep
│   └── managedIdentity.bicep
└── parameters/
    ├── dev.bicepparam
    └── prod.bicepparam

docker-compose.yml                               # Service Bus Emulator + WireMock
README.md
.gitignore
```

---

## Key NuGet Packages

| Package | Purpose |
|---------|---------|
| `Azure.Messaging.ServiceBus` | Service Bus SDK (processor model) |
| `Microsoft.Extensions.Hosting` | BackgroundService / Worker |
| `Microsoft.Extensions.Http` | IHttpClientFactory |
| `Microsoft.Extensions.Http.Resilience` | .NET 10 built-in resilience (preferred over Polly directly) |
| `Microsoft.Extensions.Http.Polly` | Alternative if MS Resilience doesn't cover all cases |
| `System.Text.Json` | JSON serialization (built-in) |
| `WireMock.Net` | Mock HTTP server for AssetHub API |
| `xunit` / `NSubstitute` / `FluentAssertions` | Testing stack |

---

## Configuration Shape (`appsettings.json`)

```jsonc
{
  "ServiceBus": {
    "FullyQualifiedNamespace": "<sbns>.servicebus.windows.net",
    "TopicName": "fieldops-events",
    "SubscriptionName": "assethub-processor",
    "MaxConcurrentCalls": 5
  },
  "AssetHub": {
    "BaseUrl": "https://api.assethub.example.com",
    "TokenUrl": "/oauth/token",
    "ClientId": "— from Key Vault —",
    "ClientSecret": "— from Key Vault —",
    "CompanyId": "company-001",
    "TokenRefreshBufferSeconds": 400
  },
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

## Docker Compose (Local Dev)

```yaml
version: "3.9"
services:
  servicebus-emulator:
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    ports:
      - "5672:5672"
    environment:
      ACCEPT_EULA: "Y"
      SQL_SA_PASSWORD: "YourStrong!Passw0rd"
    volumes:
      - ./config/servicebus-emulator.config.json:/ServiceBus_Emulator/ConfigFiles/Config.json

  wiremock:
    image: wiremock/wiremock:latest
    ports:
      - "9090:8080"
    volumes:
      - ./wiremock/__files:/home/wiremock/__files
      - ./wiremock/mappings:/home/wiremock/mappings
```

---

## Key Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| Single .NET solution with 4 class libraries + 1 host | Clean separation without over-engineering. Each layer is a project. |
| Minimal API for DLQ replay endpoint | Lightweight — no need for full MVC controllers. |
| `BackgroundService` for Service Bus subscriber | Standard .NET hosting pattern. Lifecycle managed by the host. |
| `DelegatingHandler` for OAuth token attachment | Separates auth concern from business HTTP calls. Standard `HttpMessageHandler` pipeline. |
| `IHttpClientFactory` + typed client | Testable, pooled connections, resilience wraps the handler pipeline. |
| `IOptions<T>` / `IOptionsMonitor<T>` for all config | Standard .NET config binding. Supports hot-reload where applicable. |
| Domain exceptions (not generic `Exception`) | Senior-level error handling. `ValidationException`, `DuplicateAssetException`, `AssetHubApiException`. |
| Correlation ID via `ILogger.BeginScope` | All logs for a single event carry the `eventId`. |
| Managed Identity for Azure (prod) / appsettings for local | Security best practice. No secrets in code. |

---

## Dependency Flow (References)

```
AssetMiddleware.Api
  → AssetMiddleware.Application
  → AssetMiddleware.Infrastructure
  → AssetMiddleware.Domain

AssetMiddleware.Application
  → AssetMiddleware.Domain

AssetMiddleware.Infrastructure
  → AssetMiddleware.Application (for interfaces)
  → AssetMiddleware.Domain (for models)

AssetMiddleware.Domain
  → (no project references — leaf node)
```

---

## Next Steps

Proceed to the task-specific plans:
- [01-TASK1-EVENT-SUBSCRIBER.md](./01-TASK1-EVENT-SUBSCRIBER.md)
- [02-TASK2-ASSETHUB-API-CLIENT.md](./02-TASK2-ASSETHUB-API-CLIENT.md)
- [03-TASK3-DATA-TRANSFORMER.md](./03-TASK3-DATA-TRANSFORMER.md)
- [04-TASK4-RESILIENCE-DLQ.md](./04-TASK4-RESILIENCE-DLQ.md)
- [05-EXECUTION-CHECKLIST.md](./05-EXECUTION-CHECKLIST.md)
