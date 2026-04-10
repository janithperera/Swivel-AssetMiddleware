using System.Net;
using System.Net.Http.Headers;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.Http;

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
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer content so it can be re-sent on 401 retry.
        // Without this, POST/PATCH bodies are consumed after the first send and the retry gets an empty body.
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);

        await AttachHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // On 401 — invalidate cached token, acquire a fresh one, retry once
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "Received 401 Unauthorized for {Method} {Uri}. Refreshing token and retrying.",
                request.Method, request.RequestUri);

            _tokenProvider.InvalidateToken();
            await AttachHeadersAsync(request, cancellationToken).ConfigureAwait(false);

            response.Dispose();
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task AttachHeadersAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Replace rather than add to prevent duplicate X-Company-Id headers on retry
        request.Headers.Remove("X-Company-Id");
        request.Headers.Add("X-Company-Id", _options.Value.CompanyId);
    }
}
