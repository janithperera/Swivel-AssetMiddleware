# Future Improvements

What I would add or change given more time. Items are grouped by category and roughly ordered by impact within each section.

---

## Observability & Monitoring

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **Application Insights integration** | Only `ILogger` to console/stdout | Add the App Insights SDK with distributed tracing across Service Bus → HTTP calls. Track custom metrics: asset creation rate, transformation duration, API latency percentiles. |
| **OpenTelemetry instrumentation** | No OTel signals emitted | Add `OpenTelemetry.Api` + OTLP exporter. Enable automatic instrumentation for `HttpClient` and `Azure.Messaging.ServiceBus`. This makes the system portable across observability backends. |
| **Structured correlation IDs** | Logs are human-readable but not correlated | Propagate `eventId` as a correlation ID through the entire pipeline (Service Bus → EventRouter → Handler → HTTP calls). Emit it in every log entry so a single Kusto/Log Analytics query retrieves the full trace of one event. |
| **Alerting rules** | No alerts configured | Set up alerts for: circuit breaker state changes (Open/HalfOpen), DLQ queue depth > threshold, P99 response latency > 5s, error rate > 5% over a 5-minute window. |
| **Bicep diagnostic settings** | Bicep deploys resources but no logging sinks | Add `Microsoft.Insights/diagnosticSettings` to App Service and Service Bus resources. Stream logs & metrics to a Log Analytics workspace. |
| **Health check probing AssetHub** | `/health` returns 200 but doesn't verify the integration | Add an `IHealthCheck` that calls AssetHub's status endpoint (or fetches the Active status ID). This gives operators a single URL to confirm the full integration is live. |

## Resilience & Reliability

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **Message idempotency store** | Deduplication relies on Service Bus `DeduplicationHistoryTimeWindow` (10 min) | Add an idempotency store (Redis, Azure Table Storage, or Cosmos DB) keyed by `eventId`. Check before processing — skip if already handled. Protects against redelivery beyond the dedup window. |
| **Photo upload retry** | Failure is caught and logged as warning; photo is lost | Add a separate retry loop for photo upload with shorter delays. Alternatively, queue failed uploads to a secondary topic for later retry. |
| **Graceful cache failure** | If the Active-status fetch fails on first call, the error propagates and fails the message | Add a fallback: retry the status fetch 2-3 times with backoff. If still failing, log a critical error and dead-letter the message with a clear reason rather than an unhandled exception. |
| **DLQ notification** | DLQ messages sit until an operator manually calls `/api/dlq/replay` | Emit an Event Grid event or send a Teams/Slack webhook notification when a message reaches the DLQ. Operators shouldn't need to poll. |
| **Distributed circuit breaker state** | `CircuitBreakerStateTracker` is an in-memory singleton | For multi-instance deployments, share circuit breaker state via Redis or a shared data store. Currently, each instance tracks its own state independently, which could lead to inconsistent behaviour. |

## Security

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **Image URL validation** | `RegistrationEventHandler` downloads from any URL in the event payload | Validate the URL scheme (`https://` only) and optionally whitelist allowed domains. Prevents SSRF where a malicious event could cause the middleware to request internal resources. |
| **DLQ replay endpoint authorisation** | `POST /api/dlq/replay` is unauthenticated | Add Azure AD / Entra ID authentication or API key validation. In production, this should require an admin role claim. |
| **Status endpoint sanitisation** | `/api/status` exposes `CircuitState`, success/failure counts | For production, either restrict access to internal network or strip infrastructure details from the response. |
| **Secret rotation support** | `ClientSecret` is read from configuration once at startup | Integrate with Key Vault secret rotation. Re-read the secret from Key Vault on each token refresh rather than caching it from startup config. |

## Performance

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **Status cache TTL** | Cached forever (until app restart) | Add a configurable TTL (e.g., 24 hours). If AssetHub adds or renames statuses, the middleware picks up changes without restarting. |
| **Batch message processing** | One HTTP call per event | If message volume is high, buffer N events and use a bulk-create endpoint (if AssetHub supports it). Reduces HTTP overhead significantly. |
| **HTTP timeout tuning** | Overall timeout hardcoded at 30 seconds | Measure AssetHub P99 latency in production. If it's typically < 2 seconds, reduce timeout to 10s with faster retries for better responsiveness. |
| **Single-pass deserialisation** | `EventRouter` parses JSON with `JsonDocument`, then handler deserialises again with `JsonSerializer` | Deserialise once in the router and pass the typed event object to the handler. Saves one full parse per message. |
| **Request deduplication at HTTP level** | Retries re-send identical requests | Add an `Idempotency-Key` header to POST/PATCH requests so AssetHub can de-duplicate on its side. Makes retries inherently safe. |

## Deployment & Operations

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **CI/CD pipeline** | No pipeline defined | Add a GitHub Actions / Azure Pipelines workflow: `dotnet build` → `dotnet test` → SAST scan → Docker image build → deploy to staging → smoke tests → promote to production. |
| **Blue/green deployment** | No deployment strategy documented | Use App Service deployment slots. Deploy to staging slot, run smoke tests, then swap to production. Zero-downtime rollouts. |
| **Auto-scaling** | Single instance (capacity: 1 in Bicep) | Configure App Service auto-scale rules based on CPU / message queue length. Requires addressing the distributed circuit breaker state issue above. |
| **DLQ runbook** | Replay endpoint exists but process not documented | Write an operational runbook: when to replay, how to inspect messages, what to do if replay keeps failing, escalation path. |
| **JSON schema validation** | Events are deserialised without upfront schema check | Validate incoming messages against a JSON schema before processing. Reject non-conforming messages early with a clear dead-letter reason. |

## Code Quality

| Improvement | Current State | What I Would Add |
|-------------|--------------|------------------|
| **Nullable reference types** | Not enabled project-wide | Add `<Nullable>enable</Nullable>` to all `.csproj` files. Catch potential null reference issues at compile time. |
| **Static analysis / linting** | No analysers configured | Add Roslyn analysers (e.g., `Microsoft.CodeAnalysis.NetAnalyzers`) and an `.editorconfig` to enforce consistent style. Run as part of CI. |
| **API versioning** | Single unversioned API | Add URL-based versioning (`/v1/dlq/replay`) so the contract can evolve without breaking existing clients. |
| **Test data builders** | Factory methods like `CreateValidRegistrationEvent()` | Introduce a builder pattern for complex test objects: `new RegistrationEventBuilder().WithMake("CAT").WithSite("SITE-01").Build()`. More expressive and flexible. |
