# Architecture Decisions

This document records the key architecture and design decisions made during implementation, together with the reasoning behind each choice.

---

## 1. Clean Architecture with Four Projects

**Decision:** Structure the solution as four layers — `Api`, `Application`, `Domain`, `Infrastructure` — with strict dependency direction.

**Why:**
- **Domain** has zero external dependencies. Business rules (Asset ID generation, onsite derivation, validation) are pure C# and can be tested without mocks or frameworks.
- **Application** defines interfaces (`IAssetHubClient`, `IEventHandler<T>`, `IAssetStatusCache`) and contains handlers/transformers that depend only on those abstractions.
- **Infrastructure** implements the interfaces using Azure SDK, HTTP clients, and caching. It is the only layer that knows about Azure Service Bus, OAuth, or Polly.
- **Api** is the composition root — it wires DI, maps endpoints, and starts the host. It contains no business logic.

**Trade-off:** More projects than a minimal CRUD app needs. Justified here because the middleware integrates two external systems and has non-trivial resilience requirements — the separation makes each layer independently testable and replaceable.

---

## 2. Event Routing with Typed Handlers

**Decision:** `EventRouter` parses the raw JSON, extracts `eventType`, and dispatches to the appropriate `IEventHandler<T>` implementation.

**Why:**
- Each event type has different processing logic (registration creates an asset; check-in patches onsite status). Separate handler classes keep each flow cohesive and testable.
- The router uses `JsonDocument.Parse()` to read `eventType` without deserialising the full payload. Only the matched handler deserialises to the specific event model. This avoids wasted work for unknown event types.
- Unknown event types are completed silently. This is deliberate — the middleware should not fail on events it doesn't handle, since the Service Bus topic may carry events for other subscribers.

---

## 3. Transformer / Handler Separation

**Decision:** Split the pipeline into:
- **Transformers**: Pure mapping and validation (FieldOps event → AssetHub request DTO).
- **Handlers**: Orchestration (cache lookup → transform → dedup check → create/update → photo upload).

**Why:**
- Transformers are stateless and have no I/O dependencies — they can be tested with simple input/output assertions.
- Handlers orchestrate the sequence of API calls and can be tested by mocking `IAssetHubClient` and `IAssetStatusCache`.
- If the mapping rules change (e.g., new fields, different validation), only the transformer needs updating. If the API workflow changes (e.g., additional pre/post steps), only the handler changes.

---

## 4. Exception-Driven Dead-Letter Categorisation

**Decision:** Use exception types to decide whether a message is dead-lettered or abandoned:

| Exception | Action | Reason |
|-----------|--------|--------|
| `ValidationException` | Dead-letter | Payload is invalid. Retrying won't fix it. |
| `DuplicateAssetException` | Dead-letter | Asset already exists. Idempotent by design. |
| `AssetHubApiException` | Dead-letter | API returned a permanent error (e.g., null response, asset not found for check-in). |
| `OperationCanceledException` | Abandon | App shutting down. Message should be retried after restart. |
| All other exceptions | Abandon | Assumed transient. Service Bus retries up to `MaxDeliveryCount` (5). |

**Why:**
- Permanent failures should not consume retry budget — they go straight to DLQ with a clear reason.
- Transient failures (network errors, timeouts, 5xx) benefit from retries with backoff.
- Custom exception types (`DuplicateAssetException`, `AssetHubApiException`) provide precise dead-letter reasons for operator triage.

---

## 5. Service Bus Subscriber as BackgroundService

**Decision:** `ServiceBusEventSubscriber` extends `BackgroundService` and creates a `ServiceBusProcessor`.

**Why:**
- `BackgroundService` integrates with the .NET host lifecycle — it starts automatically with the host and shuts down gracefully on SIGTERM.
- `ServiceBusProcessor` manages connection pooling, message locking, and concurrent delivery internally.
- `AutoCompleteMessages = false` gives the subscriber explicit control over completion/abandonment/dead-lettering.
- Each incoming message gets its own DI scope (`_scopeFactory.CreateScope()`) to avoid captive dependency issues — scoped services like `EventRouter` and handlers get fresh instances per message.

---

## 6. DLQ Replay via REST Endpoint

**Decision:** Expose `POST /api/dlq/replay` to re-process dead-lettered messages. The processor reads messages from the DLQ, re-sends them to the main topic (preserving `MessageId` for dedup), and tracks a `ReplayCount` in application properties.

**Why:**
- Operators need a way to retry failed messages after the root cause is fixed (e.g., AssetHub configuration corrected, invalid data patched).
- Preserving `MessageId` ensures that if the message was dead-lettered due to a transient issue that has now resolved, it can be safely re-processed.
- `ReplayCount` tracks how many times a message has been replayed, providing an audit trail and a circuit breaker for repeatedly failing messages.

---

## 7. Infrastructure as Code with Bicep

**Decision:** Define all Azure resources (App Service, Service Bus, Key Vault, Managed Identity) in Bicep templates under `infra/`.

**Why:**
- Reproducible deployments across environments (dev/prod) via parameter files.
- Bicep modules follow the same separation as the application: `appService.bicep`, `serviceBus.bicep`, `keyVault.bicep`, `managedIdentity.bicep`.
- `main.bicep` orchestrates modules and passes outputs between them (e.g., managed identity `principalId` → Key Vault access policy).
- Environment-specific values (`dev.bicepparam`, `prod.bicepparam`) keep configurations declarative and reviewable in code review.
