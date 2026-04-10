using System.Net;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AssetMiddleware.Infrastructure.Tests;

public class OAuthDelegatingHandlerTests
{
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly IOptions<AssetHubOptions> _options;
    private readonly ILogger<OAuthDelegatingHandler> _logger =
        Substitute.For<ILogger<OAuthDelegatingHandler>>();

    public OAuthDelegatingHandlerTests()
    {
        _options = Options.Create(new AssetHubOptions
        {
            BaseUrl = "https://api.example.com",
            CompanyId = "company-001"
        });

        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns("valid-token");
    }

    private OAuthDelegatingHandler CreateHandler(HttpMessageHandler innerHandler)
    {
        var handler = new OAuthDelegatingHandler(_tokenProvider, _options, _logger)
        {
            InnerHandler = innerHandler
        };
        return handler;
    }

    // ── Attaches Authorization header ─────────────────────────────────────

    [Fact]
    public async Task SendAsync_AttachesBearerTokenAndCompanyIdHeader()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.example.com/test");

        inner.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.LastRequest.Headers.Authorization.Parameter.Should().Be("valid-token");
        inner.LastRequest.Headers.GetValues("X-Company-Id").Should().ContainSingle("company-001");
    }

    // ── 401 retry ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_On401_InvalidatesTokenFetchesNewAndRetriesOnce()
    {
        var callCount = 0;
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(ci => callCount++ == 0 ? "expired-token" : "fresh-token");

        var inner = new SequentialHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/resource");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _tokenProvider.Received(1).InvalidateToken();
        inner.CallCount.Should().Be(2);
    }

    // ── 401 retry only once ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_On401AfterRetry_Returns401()
    {
        var inner = new SequentialHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/resource");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.CallCount.Should().Be(2);
    }

    // ── Content is buffered for retry ─────────────────────────────────────

    [Fact]
    public async Task SendAsync_PostContent_IsPreservedOnRetry()
    {
        var inner = new SequentialHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var content = new StringContent("{\"name\":\"test\"}");
        await client.PostAsync("https://api.example.com/create", content);

        inner.CallCount.Should().Be(2);
        // Both requests should have sent content (not null body on retry)
        inner.AllRequests.Should().AllSatisfy(r => r.Content.Should().NotBeNull());
    }

    // ── No retry on success ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_On200_DoesNotInvalidateToken()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.example.com/resource");

        _tokenProvider.DidNotReceive().InvalidateToken();
        inner.CallCount.Should().Be(1);
    }

    // ── X-Company-Id not duplicated on retry ──────────────────────────────

    [Fact]
    public async Task SendAsync_On401Retry_CompanyIdHeaderNotDuplicated()
    {
        var inner = new SequentialHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://api.example.com/resource");

        // The retry request (second) should have exactly one X-Company-Id
        inner.AllRequests.Last().Headers.GetValues("X-Company-Id").Should().HaveCount(1);
    }

    // ── Test helpers ──────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        public StubHandler(HttpStatusCode code) => _statusCode = code;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class SequentialHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _responses;
        public int CallCount { get; private set; }
        public List<HttpRequestMessage> AllRequests { get; } = [];

        public SequentialHandler(params HttpStatusCode[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            AllRequests.Add(request);
            var code = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }
}
