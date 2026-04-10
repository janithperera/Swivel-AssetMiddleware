using System.Net.Http.Json;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.AssetHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.Http;

public sealed class OAuthTokenProvider : ITokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AssetHubOptions> _options;
    private readonly ILogger<OAuthTokenProvider> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OAuthTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AssetHubOptions> options,
        ILogger<OAuthTokenProvider> logger,
        TimeProvider timeProvider)
    {
        _httpClient = httpClientFactory.CreateClient("AssetHub.Token");
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
            return _cachedToken;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null && _timeProvider.GetUtcNow() < _expiresAt)
                return _cachedToken;

            _logger.LogInformation("Refreshing OAuth token...");

            var opts = _options.Value;
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret
            });

            var response = await _httpClient.PostAsync(opts.TokenUrl, form, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content
                .ReadFromJsonAsync<TokenResponse>(ct)
                .ConfigureAwait(false)
                ?? throw new AssetHubApiException("Token response was null.");

            _cachedToken = tokenResponse.AccessToken;

            var createdAt = DateTimeOffset.FromUnixTimeSeconds(tokenResponse.CreatedAt);
            var absoluteExpiry = createdAt.AddSeconds(tokenResponse.ExpiresIn);
            _expiresAt = absoluteExpiry.AddSeconds(-opts.TokenRefreshBufferSeconds);

            _logger.LogInformation(
                "Token refreshed. Expires at {ExpiresAt}, proactive refresh at {RefreshAt}",
                absoluteExpiry, _expiresAt);

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
