using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AssetMiddleware.Infrastructure.Tests;

public class OAuthTokenProviderTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly AssetHubOptions _options;

    public OAuthTokenProviderTests()
    {
        _server = WireMockServer.Start();

        _options = new AssetHubOptions
        {
            BaseUrl = _server.Url!,
            TokenUrl = "/oauth/token",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            CompanyId = "company-001",
            TokenRefreshBufferSeconds = 400
        };

        SetupTokenStub("mock-access-token");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldReturnAccessToken()
    {
        var provider = CreateProvider();

        var token = await provider.GetTokenAsync(CancellationToken.None);

        token.Should().Be("mock-access-token");
    }

    [Fact]
    public async Task GetTokenAsync_CalledTwice_ShouldUseCachedToken()
    {
        var provider = CreateProvider();

        var token1 = await provider.GetTokenAsync(CancellationToken.None);
        var token2 = await provider.GetTokenAsync(CancellationToken.None);

        token1.Should().Be(token2);

        // Token endpoint should only be called once
        var calls = _server.LogEntries
            .Count(e => e.RequestMessage.Path?.Equals("/oauth/token") == true);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_AfterInvalidate_ShouldFetchNewToken()
    {
        var provider = CreateProvider();

        var firstToken = await provider.GetTokenAsync(CancellationToken.None);

        // Reconfigure stub to return a different token
        SetupTokenStub("new-access-token");

        provider.InvalidateToken();

        var secondToken = await provider.GetTokenAsync(CancellationToken.None);

        secondToken.Should().Be("new-access-token");
        secondToken.Should().NotBe(firstToken);
    }

    [Fact]
    public async Task GetTokenAsync_ServerError_ShouldThrow()
    {
        _server.Reset();
        _server.Given(Request.Create().WithPath("/oauth/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var provider = CreateProvider();

        var act = async () => await provider.GetTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTokenAsync_Concurrent_ShouldOnlyFetchOnce()
    {
        var provider = CreateProvider();

        // Fire 10 concurrent token requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync(CancellationToken.None));

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("mock-access-token");

        // Despite 10 callers, only one HTTP call should be made
        var calls = _server.LogEntries
            .Count(e => e.RequestMessage.Path?.Equals("/oauth/token") == true);
        calls.Should().Be(1);
    }

    private OAuthTokenProvider CreateProvider()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("AssetHub.Token")
            .Returns(new HttpClient { BaseAddress = new Uri(_server.Url!) });

        return new OAuthTokenProvider(
            factory,
            Options.Create(_options),
            NullLogger<OAuthTokenProvider>.Instance,
            TimeProvider.System);
    }

    private void SetupTokenStub(string accessToken)
    {
        _server.Given(Request.Create().WithPath("/oauth/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    access_token = accessToken,
                    token_type = "Bearer",
                    expires_in = 5400,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }));
    }

    public void Dispose() => _server.Stop();
}
