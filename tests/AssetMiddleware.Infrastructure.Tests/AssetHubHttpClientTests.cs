using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.AssetHub;
using AssetMiddleware.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AssetMiddleware.Infrastructure.Tests;

/// <summary>
/// Integration tests for AssetHubHttpClient against a local WireMock server.
/// Each test class starts its own WireMock instance — no port conflicts.
/// </summary>
public class AssetHubHttpClientTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly AssetHubHttpClient _sut;

    public AssetHubHttpClientTests()
    {
        _server = WireMockServer.Start(); // random available port

        var options = Options.Create(new AssetHubOptions
        {
            BaseUrl = _server.Url!,
            TokenUrl = "/oauth/token",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            CompanyId = "company-001",
            TokenRefreshBufferSeconds = 400
        });

        var httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!.TrimEnd('/') + "/") };

        _sut = new AssetHubHttpClient(httpClient, options, NullLogger<AssetHubHttpClient>.Instance);
    }

    // ── GetActiveStatusIdAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetActiveStatusIdAsync_ShouldReturnActiveStatusId()
    {
        StubStatusEndpoint();

        var result = await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        result.Should().Be(1);
    }

    [Fact]
    public async Task GetActiveStatusIdAsync_NoActiveStatus_ShouldThrowAssetHubApiException()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/companies/company-001/asset-statuses").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { data = new[] { new { id = 2, name = "Inactive" } } }));

        var act = async () => await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        await act.Should().ThrowAsync<AssetHubApiException>()
            .WithMessage("*Active status*");
    }

    [Fact]
    public async Task GetActiveStatusIdAsync_ServerError_ShouldThrow()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/companies/company-001/asset-statuses").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var act = async () => await _sut.GetActiveStatusIdAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── SearchAssetByIdAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SearchAssetByIdAsync_AssetExists_ShouldReturnResult()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-001/assets")
                .WithParam("search", "Caterpillar-320-SN-9901")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    data = new[]
                    {
                        new { id = "internal-001", assetId = "Caterpillar-320-SN-9901", onsite = true }
                    }
                }));

        var result = await _sut.SearchAssetByIdAsync("proj-001", "Caterpillar-320-SN-9901", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("internal-001");
        result.AssetId.Should().Be("Caterpillar-320-SN-9901");
    }

    [Fact]
    public async Task SearchAssetByIdAsync_NoMatch_ShouldReturnNull()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-001/assets").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { data = Array.Empty<object>() }));

        var result = await _sut.SearchAssetByIdAsync("proj-001", "Unknown-Asset-ID", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── CreateAssetAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateAssetAsync_ShouldReturnCreatedAsset()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-001/assets").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new
                {
                    data = new { id = "asset-001", assetId = "Caterpillar-320-SN-9901" }
                }));

        var request = new CreateAssetRequest
        {
            AssetId = "Caterpillar-320-SN-9901",
            Name = "Caterpillar 320 Excavator",
            Make = "Caterpillar",
            Model = "320",
            SerialNumber = "SN-9901",
            StatusId = 1,
            Ownership = "Subcontracted",
            ProjectId = "proj-001"
        };

        var result = await _sut.CreateAssetAsync("proj-001", request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Data.Id.Should().Be("asset-001");
        result.Data.AssetId.Should().Be("Caterpillar-320-SN-9901");
    }

    [Fact]
    public async Task CreateAssetAsync_ServerError_ShouldThrow()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-error/assets").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var request = new CreateAssetRequest
        {
            AssetId = "X-Y-Z",
            Name = "Test",
            Make = "X",
            Model = "Y",
            SerialNumber = "Z",
            StatusId = 1,
            Ownership = "Subcontracted",
            ProjectId = "proj-error"
        };

        var act = async () => await _sut.CreateAssetAsync("proj-error", request, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── UpdateAssetAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAssetAsync_ShouldReturnUpdatedAsset()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-001/assets/asset-001").UsingPatch())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    data = new { id = "asset-001", onsite = true }
                }));

        var request = new UpdateAssetRequest { Onsite = true };

        var result = await _sut.UpdateAssetAsync("proj-001", "asset-001", request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Data.Id.Should().Be("asset-001");
        result.Data.Onsite.Should().BeTrue();
    }

    // ── UploadPhotoAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UploadPhotoAsync_ShouldCompleteSuccessfully()
    {
        _server.Given(Request.Create()
                .WithPath("/v1/projects/proj-001/assets/asset-001/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        using var stream = new MemoryStream("fake-image-bytes"u8.ToArray());

        var act = async () => await _sut.UploadPhotoAsync(
            "proj-001", "asset-001", stream, "photo.jpg", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void StubStatusEndpoint() =>
        _server.Given(Request.Create()
                .WithPath("/v1/companies/company-001/asset-statuses").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    data = new[]
                    {
                        new { id = 1, name = "Active" },
                        new { id = 2, name = "Inactive" }
                    }
                }));

    public void Dispose() => _server.Stop();
}
