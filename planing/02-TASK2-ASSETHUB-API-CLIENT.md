# Task 2 — AssetHub API Client (OAuth 2.0 + Asset Endpoints)

## Goal
Build a typed HTTP client that authenticates via OAuth 2.0 client credentials, proactively refreshes tokens, and implements all AssetHub CRUD endpoints. Must be injectable and testable with WireMock.

---

## Step-by-Step Implementation

### Step 2.1 — Define the Interface (Application Layer)

**File:** `Application/Interfaces/IAssetHubClient.cs`

```csharp
public interface IAssetHubClient
{
    Task<int> GetActiveStatusIdAsync(CancellationToken ct);
    Task<AssetSearchResult?> SearchAssetByIdAsync(string projectId, string assetId, CancellationToken ct);
    Task<CreateAssetResponse> CreateAssetAsync(string projectId, CreateAssetRequest request, CancellationToken ct);
    Task<UpdateAssetResponse> UpdateAssetAsync(string projectId, string id, UpdateAssetRequest request, CancellationToken ct);
    Task UploadPhotoAsync(string projectId, string id, Stream photoStream, string fileName, CancellationToken ct);
}
```

**File:** `Application/Interfaces/ITokenProvider.cs`

```csharp
public interface ITokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
    void InvalidateToken(); // Called on 401 to force refresh
}
```

**File:** `Application/Interfaces/IAssetStatusCache.cs`

```csharp
public interface IAssetStatusCache
{
    Task<int> GetActiveStatusIdAsync(CancellationToken ct);
}
```

---

### Step 2.2 — Define AssetHub Configuration

**File:** `Application/Configuration/AssetHubOptions.cs`

```csharp
public sealed class AssetHubOptions
{
    public const string SectionName = "AssetHub";

    public string BaseUrl { get; init; } = default!;
    public string TokenUrl { get; init; } = "/oauth/token";
    public string ClientId { get; init; } = default!;
    public string ClientSecret { get; init; } = default!;
    public string CompanyId { get; init; } = default!;
    public int TokenRefreshBufferSeconds { get; init; } = 400;
    // 5400 - 400 = 5000 seconds → proactive refresh at 5000s
}
```

---

### Step 2.3 — Define Response/Request DTOs (Domain Layer)

**File:** `Domain/Models/AssetHub/TokenResponse.cs`
```csharp
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = default!;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = default!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }
}
```

**File:** `Domain/Models/AssetHub/CreateAssetRequest.cs`
```csharp
public sealed class CreateAssetRequest
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    [JsonPropertyName("make")]
    public string Make { get; init; } = default!;

    [JsonPropertyName("model")]
    public string Model { get; init; } = default!;

    [JsonPropertyName("statusId")]
    public int StatusId { get; init; }

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; init; } = default!;

    [JsonPropertyName("yearMfg")]
    public string? YearMfg { get; init; }

    [JsonPropertyName("ratePerHour")]
    public decimal? RatePerHour { get; init; }

    [JsonPropertyName("ownership")]
    public string Ownership { get; init; } = "Subcontracted"; // ALWAYS

    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = default!;
}
```

**File:** `Domain/Models/AssetHub/UpdateAssetRequest.cs`
```csharp
public sealed class UpdateAssetRequest
{
    [JsonPropertyName("onsite")]
    public bool? Onsite { get; init; }
}
```

**File:** `Domain/Models/AssetHub/AssetSearchResponse.cs`
```csharp
public sealed class AssetSearchResponse
{
    [JsonPropertyName("data")]
    public List<AssetSearchResult> Data { get; init; } = [];
}

public sealed class AssetSearchResult
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;

    [JsonPropertyName("onsite")]
    public bool Onsite { get; init; }
}
```

**File:** `Domain/Models/AssetHub/CreateAssetResponse.cs`
```csharp
public sealed class CreateAssetResponse
{
    [JsonPropertyName("data")]
    public AssetData Data { get; init; } = default!;
}

public sealed class AssetData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;
}
```

**File:** `Domain/Models/AssetHub/UpdateAssetResponse.cs`
```csharp
public sealed class UpdateAssetResponse
{
    [JsonPropertyName("data")]
    public UpdateAssetData Data { get; init; } = default!;
}

public sealed class UpdateAssetData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("onsite")]
    public bool Onsite { get; init; }
}
```

**File:** `Domain/Models/AssetHub/AssetStatusResponse.cs`
```csharp
public sealed class AssetStatusResponse
{
    [JsonPropertyName("data")]
    public List<AssetStatus> Data { get; init; } = [];
}

public sealed class AssetStatus
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;
}
```

---

### Step 2.4 — Implement OAuthTokenProvider (Infrastructure Layer)

**File:** `Infrastructure/Http/OAuthTokenProvider.cs`

This is the **most critical class** for Task 2. It handles:
1. Token acquisition via client credentials
2. Proactive refresh at 5000 seconds (not waiting for 401)
3. Thread-safe token caching with `SemaphoreSlim`
4. Token invalidation on 401 (called by the delegating handler)

```csharp
public sealed class OAuthTokenProvider : ITokenProvider
{
    private readonly HttpClient _httpClient; // Separate named client (no auth handler!)
    private readonly IOptions<AssetHubOptions> _options;
    private readonly ILogger<OAuthTokenProvider> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeProvider _timeProvider; // For testability

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OAuthTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AssetHubOptions> options,
        ILogger<OAuthTokenProvider> logger,
        TimeProvider timeProvider)
    {
        // IMPORTANT: Use a named client WITHOUT the auth delegating handler
        // to avoid circular dependency (auth handler → token provider → http client → auth handler)
        _httpClient = httpClientFactory.CreateClient("AssetHub.Token");
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Fast path — token is still valid
        if (_cachedToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
            return _cachedToken;

        // Slow path — acquire semaphore and refresh
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock (another thread may have refreshed)
            if (_cachedToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
                return _cachedToken;

            _logger.LogInformation("Refreshing OAuth token...");

            var opts = _options.Value;
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret
            });

            var response = await _httpClient.PostAsync(opts.TokenUrl, request, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content
                .ReadFromJsonAsync<TokenResponse>(ct)
                .ConfigureAwait(false)
                ?? throw new AssetHubApiException("Token response was null");

            _cachedToken = tokenResponse.AccessToken;

            // PROACTIVE REFRESH CALCULATION:
            // Token was created at `created_at` (Unix timestamp)
            // Token expires at `created_at + expires_in`
            // Refresh at `created_at + expires_in - buffer`
            // Buffer = 400 seconds → refresh at 5000s into a 5400s token
            var createdAt = DateTimeOffset.FromUnixTimeSeconds(tokenResponse.CreatedAt);
            var expiresAt = createdAt.AddSeconds(tokenResponse.ExpiresIn);
            _expiresAt = expiresAt.AddSeconds(-opts.TokenRefreshBufferSeconds);

            _logger.LogInformation(
                "Token refreshed. Expires at {ExpiresAt}, will refresh at {RefreshAt}",
                expiresAt, _expiresAt);

            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void InvalidateToken()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
        _logger.LogWarning("OAuth token invalidated. Will refresh on next request.");
    }
}
```

**Key design decisions:**

| Decision | Why |
|----------|-----|
| `SemaphoreSlim` for thread safety | Multiple concurrent messages may need a token simultaneously. Only one should refresh. |
| Double-check pattern | After acquiring the lock, check again — another thread may have already refreshed. |
| `TimeProvider` injected | Testable — you can mock time in unit tests to test expiry logic. |
| Separate named HttpClient for token endpoint | **Avoids circular dependency**. The auth delegating handler calls `ITokenProvider.GetTokenAsync()`. If the token client also has the auth handler, infinite loop. |
| `created_at + expires_in - buffer` for refresh | Proactive refresh at 5000s into a 5400s token. Exactly as specified. |

---

### Step 2.5 — Implement OAuth Delegating Handler

**File:** `Infrastructure/Http/OAuthDelegatingHandler.cs`

This `DelegatingHandler` sits in the `HttpClient` pipeline and:
1. Attaches `Authorization: Bearer {token}` to every request
2. Attaches `X-Company-Id: {companyId}` to every request
3. On 401 → invalidates token, gets a new one, retries once

```csharp
public sealed class OAuthDelegatingHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly IOptions<AssetHubOptions> _options;
    private readonly ILogger<OAuthDelegatingHandler> _logger;

    public OAuthDelegatingHandler(
        ITokenProvider tokenProvider,
        IOptions<AssetHubOptions> options,
        ILogger<OAuthDelegatingHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        await AttachHeadersAsync(request, ct).ConfigureAwait(false);

        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        // On 401 → invalidate, re-acquire, retry ONCE
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401. Invalidating token and retrying...");

            _tokenProvider.InvalidateToken();
            await AttachHeadersAsync(request, ct).ConfigureAwait(false);

            response.Dispose();
            response = await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        return response;
    }

    private async Task AttachHeadersAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Remove("X-Company-Id");
        request.Headers.Add("X-Company-Id", _options.Value.CompanyId);
    }
}
```

**Note on 401 retry:** The request body may have been consumed. For `POST`/`PATCH` with content, you need to handle this. Options:
- Buffer the content before sending (e.g., `LoadIntoBufferAsync`)
- Or create a new request clone

A production-grade approach would clone the request. Add this helper:

```csharp
// In OAuthDelegatingHandler — improved version
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
{
    // Buffer content so it can be re-read on retry
    if (request.Content is not null)
        await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);

    await AttachHeadersAsync(request, ct).ConfigureAwait(false);
    var response = await base.SendAsync(request, ct).ConfigureAwait(false);

    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        _logger.LogWarning("Received 401. Invalidating token and retrying...");
        _tokenProvider.InvalidateToken();
        await AttachHeadersAsync(request, ct).ConfigureAwait(false);
        response.Dispose();
        response = await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    return response;
}
```

---

### Step 2.6 — Implement AssetHub HTTP Client (Infrastructure Layer)

**File:** `Infrastructure/Http/AssetHubHttpClient.cs`

```csharp
public sealed class AssetHubHttpClient : IAssetHubClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AssetHubHttpClient> _logger;

    public AssetHubHttpClient(HttpClient httpClient, ILogger<AssetHubHttpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> GetActiveStatusIdAsync(CancellationToken ct)
    {
        // This call uses companyId — from config
        // The path will be set during DI registration via BaseAddress
        var response = await _httpClient.GetAsync("v1/companies/{companyId}/asset-statuses", ct)
            .ConfigureAwait(false);
        // NOTE: companyId is replaced during registration — see DI setup
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AssetStatusResponse>(ct)
            .ConfigureAwait(false);

        var active = result?.Data.FirstOrDefault(s => 
            s.Name.Equals("Active", StringComparison.OrdinalIgnoreCase))
            ?? throw new AssetHubApiException("Active status not found in AssetHub");

        return active.Id;
    }

    public async Task<AssetSearchResult?> SearchAssetByIdAsync(
        string projectId, string assetId, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets?search={Uri.EscapeDataString(assetId)}";
        var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AssetSearchResponse>(ct)
            .ConfigureAwait(false);

        // Return first match if any, null if empty result
        return result?.Data.FirstOrDefault();
    }

    public async Task<CreateAssetResponse> CreateAssetAsync(
        string projectId, CreateAssetRequest request, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets";

        var response = await _httpClient.PostAsJsonAsync(url, request, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<CreateAssetResponse>(ct)
            .ConfigureAwait(false)
            ?? throw new AssetHubApiException("Create asset response was null");
    }

    public async Task<UpdateAssetResponse> UpdateAssetAsync(
        string projectId, string id, UpdateAssetRequest request, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets/{Uri.EscapeDataString(id)}";

        var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(request)
        };

        var response = await _httpClient.SendAsync(httpRequest, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<UpdateAssetResponse>(ct)
            .ConfigureAwait(false)
            ?? throw new AssetHubApiException("Update asset response was null");
    }

    public async Task UploadPhotoAsync(
        string projectId, string id, Stream photoStream, string fileName, CancellationToken ct)
    {
        var url = $"v1/projects/{Uri.EscapeDataString(projectId)}/assets/{Uri.EscapeDataString(id)}/attachments";

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(photoStream);
        content.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync(url, content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Photo uploaded for asset {AssetId} in project {ProjectId}", id, projectId);
    }
}
```

---

### Step 2.7 — Implement Asset Status Cache

**File:** `Infrastructure/Caching/AssetStatusCache.cs`

```csharp
public sealed class AssetStatusCache : IAssetStatusCache
{
    private readonly IAssetHubClient _client;
    private readonly ILogger<AssetStatusCache> _logger;
    private int? _activeStatusId;

    public AssetStatusCache(IAssetHubClient client, ILogger<AssetStatusCache> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> GetActiveStatusIdAsync(CancellationToken ct)
    {
        if (_activeStatusId.HasValue)
            return _activeStatusId.Value;

        _activeStatusId = await _client.GetActiveStatusIdAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Cached Active status ID: {StatusId}", _activeStatusId);
        return _activeStatusId.Value;
    }
}
```

---

### Step 2.8 — DI Registration for HTTP Clients

**File:** `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (partial)

```csharp
public static IServiceCollection AddAssetHubClient(
    this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AssetHubOptions>(
        configuration.GetSection(AssetHubOptions.SectionName));

    // Register the token provider as singleton (caches token)
    services.AddSingleton<ITokenProvider, OAuthTokenProvider>();

    // Register the delegating handler as transient (standard for DelegatingHandler)
    services.AddTransient<OAuthDelegatingHandler>();

    // Named HTTP client for token endpoint (NO auth handler — avoids circular dep)
    services.AddHttpClient("AssetHub.Token", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<AssetHubOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl);
    });

    // Typed HTTP client for AssetHub API (WITH auth handler in pipeline)
    services.AddHttpClient<IAssetHubClient, AssetHubHttpClient>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<AssetHubOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    })
    .AddHttpMessageHandler<OAuthDelegatingHandler>();
    // Resilience policies are added in Task 4

    // Asset status cache — singleton, caches on first call
    services.AddSingleton<IAssetStatusCache, AssetStatusCache>();

    return services;
}
```

**Critical points:**
- **Two HTTP clients**: `"AssetHub.Token"` (named, no auth) and typed `IAssetHubClient` (with auth handler).
- `OAuthDelegatingHandler` is `Transient` — standard lifetime for `DelegatingHandler` in the `IHttpClientFactory` pipeline.
- `ITokenProvider` is `Singleton` — it caches the token in-memory.
- `BaseAddress` ends with `/` — required for relative URI resolution in `HttpClient`.

---

### Step 2.9 — WireMock Setup

**File:** `AssetMiddleware.MockServer/Program.cs` (or helper in test project)

```csharp
var server = WireMockServer.Start(new WireMockServerSettings
{
    Port = 9090,
    Logger = new WireMockConsoleLogger()
});

Console.WriteLine($"WireMock running at {server.Url}");

// 1. Token endpoint
server.Given(Request.Create().WithPath("/oauth/token").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithBodyAsJson(new
        {
            access_token = "mock-token-" + Guid.NewGuid().ToString("N")[..8],
            token_type = "Bearer",
            expires_in = 5400,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));

// 2. Asset statuses
server.Given(Request.Create()
        .WithPath("/v1/companies/*/asset-statuses").UsingGet())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithBodyAsJson(new
        {
            data = new[]
            {
                new { id = 1, name = "Active" },
                new { id = 2, name = "Inactive" },
                new { id = 3, name = "Retired" }
            }
        }));

// 3. Asset search (default: no match — safe to create)
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets").UsingGet())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithBodyAsJson(new { data = Array.Empty<object>() }));

// 4. Asset create
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(201)
        .WithBodyAsJson(new
        {
            data = new { id = "asset-" + Guid.NewGuid().ToString("N")[..6], assetId = "dynamic" }
        }));

// 5. Asset update (PATCH)
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets/*").UsingPatch())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithBodyAsJson(new { data = new { id = "asset-001", onsite = true } }));

// 6. Photo upload
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets/*/attachments").UsingPost())
    .RespondWith(Response.Create().WithStatusCode(201));

Console.ReadLine();
server.Stop();
```

---

### Step 2.10 — Rate Limiting Handler (Optional)

**File:** `Infrastructure/Http/RateLimitDelegatingHandler.cs`

```csharp
public sealed class RateLimitDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<RateLimitDelegatingHandler> _logger;

    public RateLimitDelegatingHandler(ILogger<RateLimitDelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(5);

            _logger.LogWarning("Rate limited. Retrying after {RetryAfter}", retryAfter);

            await Task.Delay(retryAfter, ct).ConfigureAwait(false);

            response.Dispose();
            response = await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        return response;
    }
}
```

---

## Checklist for Task 2

- [ ] `IAssetHubClient` interface defined with all endpoints
- [ ] `ITokenProvider` interface with `GetTokenAsync` + `InvalidateToken`
- [ ] `TokenResponse` DTO with correct `JsonPropertyName` attributes
- [ ] All request/response DTOs created
- [ ] `OAuthTokenProvider` — proactive refresh at `created_at + expires_in - 400`
- [ ] Thread-safe token caching with `SemaphoreSlim` + double-check
- [ ] `OAuthDelegatingHandler` — attaches `Bearer` token + `X-Company-Id` to every request
- [ ] 401 recovery — invalidate → re-acquire → retry once
- [ ] `AssetHubHttpClient` — typed client implementing all endpoints
- [ ] Dedup check in `SearchAssetByIdAsync` — empty `data` array = safe to create
- [ ] `AssetStatusCache` — caches Active status ID on first call
- [ ] Two HTTP clients: token client (no auth) + API client (with auth handler)
- [ ] `IHttpClientFactory` used — never `new HttpClient()`
- [ ] `IOptions<AssetHubOptions>` for all configuration
- [ ] `BaseAddress` ends with `/` for relative URI resolution
- [ ] `Uri.EscapeDataString` for path parameters
- [ ] `ConfigureAwait(false)` throughout
- [ ] `CancellationToken` passed to all async methods
- [ ] WireMock stubs for all endpoints
- [ ] Rate limiting handler (optional)

---

## Common Pitfalls to Avoid

1. **Circular dependency**: Token HTTP client must NOT have the `OAuthDelegatingHandler`. Use a separate named client.
2. **`new HttpClient()`**: Never. Always `IHttpClientFactory`.
3. **Reactive token refresh only**: Must refresh proactively at 5000s. Use `created_at + expires_in - buffer`.
4. **Missing `X-Company-Id` header**: Required on every request. Easy to forget.
5. **`BaseAddress` without trailing slash**: `HttpClient` relative URI resolution breaks without it.
6. **Not buffering request content for 401 retry**: POST/PATCH body is consumed after first send. Must `LoadIntoBufferAsync()`.
