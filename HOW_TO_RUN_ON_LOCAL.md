# Running AssetMiddleware Locally

> This guide is written for **Apple Silicon** (M1/M2/M3/M4) Macs running Docker Desktop with Rosetta emulation enabled.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` to verify |
| Docker Desktop | Latest | Enable **Rosetta** in Settings → General |
| Git | Any | — |

> **Rosetta requirement:** the Azure Service Bus Emulator depends on `mcr.microsoft.com/azure-sql-edge`, which is an `linux/amd64` image. It runs under Rosetta on Apple Silicon. Without Rosetta enabled in Docker Desktop, the `sqledge` container will fail to start.
>
> Enable it: **Docker Desktop → Settings → General → "Use Rosetta for x86_64/amd64 emulation on Apple Silicon"** → Apply & Restart.

---

## Environment Setup

### 1 — Clone the repository

```bash
git clone <repo-url>
cd Swivel-AssetMiddleware
```

### 2 — Create `appsettings.Development.json`

> **This file is excluded from `.gitignore`.** Do not commit it.

Create the file at `src/AssetMiddleware.Api/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },

  "ServiceBus": {
    "ConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "fieldops-events",
    "SubscriptionName": "asset-middleware",
    "MaxConcurrentCalls": 2
  },

  "AssetHub": {
    "BaseUrl": "http://localhost:9090",
    "TokenUrl": "/oauth/token",
    "ClientId": "dev-client-id",
    "ClientSecret": "dev-client-secret",
    "CompanyId": "dev-company-id",
    "TokenRefreshBufferSeconds": 400
  }
}
```

The `SAS_KEY_VALUE` placeholder is intentional — the Service Bus Emulator does not validate the shared access key locally.

---

## Running Locally

### Step 1 — Start external dependencies

```bash
docker compose up -d
```

This starts three containers:

| Container | Purpose | Port(s) |
|---|---|---|
| `assethub-mock` | WireMock — mocks the AssetHub REST API | `9090` |
| `servicebus-emulator` | Azure Service Bus Emulator | `5672` (AMQP), `9354` (HTTP) |
| `sqledge` | SQL Edge — backing store for the emulator | internal only |

The Service Bus Emulator is pre-configured via `config/servicebus-config.json`:

| Setting | Value |
|---|---|
| Namespace | `sbemulatorns` |
| Topic | `fieldops-events` |
| Subscription | `asset-middleware` |
| DLQ | `asset-middleware/$DeadLetterQueue` |

Verify all containers are healthy before continuing:

```bash
docker compose ps
```

All three containers should show `running` or `healthy`. The `sqledge` container typically takes 20–30 seconds to be ready on Apple Silicon — the `servicebus-emulator` will wait for it automatically.

### Step 2 — Build the solution

```bash
dotnet build
```

### Step 3 — Run the API

```bash
cd src/AssetMiddleware.Api
dotnet run
```

Or use the VS Code launch configurations (`.vscode/launch.json`):
- **Development** — runs with `ASPNETCORE_ENVIRONMENT=Development`, opens Scalar UI automatically
- **Production** — runs with `ASPNETCORE_ENVIRONMENT=Production`

Once started, the API is available at:

| URL | Purpose |
|---|---|
| `http://localhost:5088` | API base |
| `https://localhost:7237` | API base (HTTPS) |
| `https://localhost:7237/scalar/v1` | Scalar OpenAPI UI (Development only) |
| `https://localhost:7237/openapi/v1.json` | OpenAPI spec (Development only) |
| `https://localhost:7237/health` | Health check |

### Step 4 — Send a test event

Publish a message to the Service Bus Emulator (topic `fieldops-events`) with one of the payloads below.

**Asset registration:**

```json
{
  "eventType": "asset.registration.submitted",
  "eventId":   "evt-a1b2c3",
  "projectId": "proj-9001",
  "siteRef":   "SITE-AU-042",
  "fields": {
    "assetName":    "Caterpillar 320 Excavator",
    "make":         "Caterpillar",
    "model":        "320",
    "serialNumber": "SN-9901",
    "yearMfg":      "2021",
    "category":     "Earthmoving",
    "type":         "Excavator",
    "ratePerHour":  "220.00",
    "supplier":     "Hastings Deering Pty Ltd"
  },
  "imageUrl": "https://storage.example.com/assets/SN-9901.jpg"
}
```

**Asset check-in:**

```json
{
  "eventType":    "asset.checkin.updated",
  "eventId":      "evt-d4e5f6",
  "projectId":    "proj-9001",
  "siteRef":      "SITE-AU-042",
  "serialNumber": "SN-9901",
  "make":         "Caterpillar",
  "model":        "320",
  "checkInDate":  "2026-05-06T07:00:00+10:00",
  "checkOutDate": null
}
```

### Step 5 — Trigger DLQ replay (optional)

```bash
curl -X POST http://localhost:5088/api/dlq/replay
```

---

## Running Tests

```bash
dotnet test
```

Infrastructure tests spin up their own `WireMockServer` on a random port — `docker compose` does not need to be running for `dotnet test`.

| Project | Tests | What is covered |
|---|---|---|
| `AssetMiddleware.Domain.Tests` | 13 | `AssetIdGenerator`, `OnsiteDerivation`, validation rules |
| `AssetMiddleware.Application.Tests` | 28 | `AssetTransformer`, `RegistrationEventHandler`, `CheckInEventHandler` |
| `AssetMiddleware.Infrastructure.Tests` | 14 | `AssetHubHttpClient` + `OAuthTokenProvider` against WireMock |

---

## Stopping the environment

```bash
docker compose down
```

To also remove volumes (resets the Service Bus Emulator state):

```bash
docker compose down -v
```

---

## Local vs Deployed configuration

| Setting | Local development | Deployed (Azure) |
|---|---|---|
| Service Bus | Emulator connection string in `appsettings.Development.json` | Managed Identity + fully-qualified namespace |
| AssetHub credentials | Plain values in `appsettings.Development.json` | Key Vault references in App Service config |
| AssetHub base URL | `http://localhost:9090` (WireMock) | `https://api.assethub.example.com` |

In production, no connection strings or secrets appear in `appsettings.json`. App Service is configured with `@Microsoft.KeyVault(SecretUri=...)` references for `ClientId` and `ClientSecret`, and the user-assigned Managed Identity is used for Service Bus authentication.

---

## Troubleshooting

**`sqledge` container exits immediately**
Rosetta is not enabled. Go to Docker Desktop → Settings → General → enable "Use Rosetta for x86_64/amd64 emulation on Apple Silicon", then `docker compose down -v && docker compose up -d`.

**`servicebus-emulator` shows `starting` indefinitely**
The emulator waits for `sqledge` to be fully ready. On Apple Silicon this can take up to 60 seconds on first run. Wait and re-check with `docker compose ps`.

**Port `9090` already in use**
Another process is using WireMock's port. Find and stop it: `lsof -ti:9090 | xargs kill`.

**`dotnet run` fails with `Unable to connect to Service Bus`**
Ensure `docker compose up -d` was run first and all containers are healthy before starting the API.
