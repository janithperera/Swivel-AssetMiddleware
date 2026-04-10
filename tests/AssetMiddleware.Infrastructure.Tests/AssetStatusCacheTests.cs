using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AssetMiddleware.Infrastructure.Tests;

public class AssetStatusCacheTests
{
    private readonly IAssetHubClient _client = Substitute.For<IAssetHubClient>();
    private readonly ILogger<AssetStatusCache> _logger =
        Substitute.For<ILogger<AssetStatusCache>>();
    private readonly AssetStatusCache _sut;

    public AssetStatusCacheTests()
    {
        _sut = new AssetStatusCache(_client, _logger);
    }

    // ── First call fetches from client ────────────────────────────────────

    [Fact]
    public async Task GetActiveStatusIdAsync_FirstCall_FetchesFromClient()
    {
        _client.GetActiveStatusIdAsync(Arg.Any<CancellationToken>()).Returns(42);

        var result = await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        result.Should().Be(42);
        await _client.Received(1).GetActiveStatusIdAsync(Arg.Any<CancellationToken>());
    }

    // ── Second call returns cached value ──────────────────────────────────

    [Fact]
    public async Task GetActiveStatusIdAsync_SecondCall_ReturnsCachedWithoutFetching()
    {
        _client.GetActiveStatusIdAsync(Arg.Any<CancellationToken>()).Returns(42);

        await _sut.GetActiveStatusIdAsync(CancellationToken.None);
        var result = await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        result.Should().Be(42);
        await _client.Received(1).GetActiveStatusIdAsync(Arg.Any<CancellationToken>());
    }

    // ── Concurrent access — client called exactly once ────────────────────

    [Fact]
    public async Task GetActiveStatusIdAsync_ConcurrentCalls_FetchOnlyOnce()
    {
        var tcs = new TaskCompletionSource<int>();
        _client.GetActiveStatusIdAsync(Arg.Any<CancellationToken>()).Returns(tcs.Task);

        // Fire 10 concurrent requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetActiveStatusIdAsync(CancellationToken.None))
            .ToList();

        // Complete the single HTTP call
        tcs.SetResult(99);

        var results = await Task.WhenAll(tasks);

        results.Should().AllBeEquivalentTo(99);
        await _client.Received(1).GetActiveStatusIdAsync(Arg.Any<CancellationToken>());
    }

    // ── Client failure propagates ─────────────────────────────────────────

    [Fact]
    public async Task GetActiveStatusIdAsync_ClientThrows_ExceptionPropagates()
    {
        _client.GetActiveStatusIdAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("API down"));

        var act = async () => await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("API down");
    }
}
