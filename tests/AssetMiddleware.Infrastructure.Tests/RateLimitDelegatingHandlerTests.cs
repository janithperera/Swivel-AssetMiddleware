using System.Net;
using System.Net.Http.Headers;
using AssetMiddleware.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AssetMiddleware.Infrastructure.Tests;

public class RateLimitDelegatingHandlerTests
{
    private readonly ILogger<RateLimitDelegatingHandler> _logger =
        Substitute.For<ILogger<RateLimitDelegatingHandler>>();

    private RateLimitDelegatingHandler CreateHandler(HttpMessageHandler innerHandler)
    {
        var handler = new RateLimitDelegatingHandler(_logger)
        {
            InnerHandler = innerHandler
        };
        return handler;
    }

    // ── 200 OK — no retry ─────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_SuccessResponse_ReturnsImmediately()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(1);
    }

    // ── 429 with Retry-After header — waits and retries ───────────────────

    [Fact]
    public async Task SendAsync_429WithRetryAfter_WaitsAndRetries()
    {
        var inner = new SequentialHandler(
            CreateRateLimitResponse(retryAfterSeconds: 1),
            new HttpResponseMessage(HttpStatusCode.OK));

        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(2);
    }

    // ── 429 without Retry-After — defaults to 5 seconds ──────────────────

    [Fact]
    public async Task SendAsync_429WithoutRetryAfter_UsesDefaultDelay()
    {
        // 429 without Retry-After header
        var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var inner = new SequentialHandler(rateLimitResponse, new HttpResponseMessage(HttpStatusCode.OK));

        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        // Note: this test will wait the default 5s — kept to validate the fallback.
        // In CI we could consider lowering, but the handler doesn't expose the default.
        var response = await client.GetAsync("https://api.example.com/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(2);
    }

    // ── 429 on retry too — returns 429 ────────────────────────────────────

    [Fact]
    public async Task SendAsync_429OnRetry_Returns429()
    {
        var inner = new SequentialHandler(
            CreateRateLimitResponse(retryAfterSeconds: 1),
            CreateRateLimitResponse(retryAfterSeconds: 1));

        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/test");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.CallCount.Should().Be(2);
    }

    // ── Non-429 error — no retry ──────────────────────────────────────────

    [Fact]
    public async Task SendAsync_500Error_DoesNotRetry()
    {
        var inner = new StubHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.example.com/test");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        inner.CallCount.Should().Be(1);
    }

    // ── Cancellation during delay ─────────────────────────────────────────

    [Fact]
    public async Task SendAsync_CancelledDuringWait_ThrowsOperationCancelled()
    {
        var inner = new SequentialHandler(
            CreateRateLimitResponse(retryAfterSeconds: 60),
            new HttpResponseMessage(HttpStatusCode.OK));

        using var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await client.GetAsync("https://api.example.com/test", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static HttpResponseMessage CreateRateLimitResponse(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public int CallCount { get; private set; }

        public StubHandler(HttpStatusCode code) => _statusCode = code;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class SequentialHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses;
        public int CallCount { get; private set; }

        public SequentialHandler(params HttpResponseMessage[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var response = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(response);
        }
    }
}
