# Assumptions

This document lists all assumptions made during the implementation of the AssetMiddleware integration service. These should be validated with the FieldOps and AssetHub platform owners before going to production.

---

## Domain & Business Rules

| # | Assumption | Rationale |
|---|-----------|-----------|
| 1 | **Asset ID is always derivable from Make, Model, and SerialNumber** | The spec defines `Make-Model-SerialNumber` as the format. We assume all three fields are always present on registration events and that the combination is globally unique within a project context. |
| 2 | **Only two event types exist** | `asset.registration.submitted` and `asset.checkin.updated`. Unknown event types are completed silently without processing. We assume FieldOps will not introduce new event types without a corresponding middleware update. |

## Token & Authentication

| # | Assumption | Rationale |
|---|-----------|-----------|
| 9 | **Token lifetime is communicated via `created_at` + `expires_in`** | We compute absolute expiry as `DateTimeOffset.FromUnixTimeSeconds(created_at).AddSeconds(expires_in)`. We assume the AssetHub token endpoint always returns both fields and that `created_at` is a Unix timestamp in seconds. |
| 10 | **Proactive refresh at T-400s is sufficient** | We refresh the token 400 seconds before it expires (at ~83 minutes into a 90-minute token). This buffer accounts for clock skew and concurrent request bursts. We assume network latency to the token endpoint is well under 400 seconds. |
| 11 | **A single 401 retry is enough to recover** | The `OAuthDelegatingHandler` retries a request exactly once after a 401 by invalidating and re-fetching the token. We assume 401s are caused by token expiry, not by permanent authorization failures. |
| 12 | **`X-Company-Id` is static per deployment** | The header value comes from configuration and does not change per request or per project. We assume all API calls are scoped to a single company. |
