using AssetMiddleware.Application.Handlers;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Application.Transformers;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.AssetHub;
using AssetMiddleware.Domain.Models.Events;
using FluentAssertions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AssetMiddleware.Application.Tests.HandlerTests;

public class RegistrationEventHandlerTests
{
    private readonly IAssetHubClient _client = Substitute.For<IAssetHubClient>();
    private readonly IAssetTransformer _transformer = new AssetTransformer();
    private readonly IAssetStatusCache _statusCache = Substitute.For<IAssetStatusCache>();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();

    private RegistrationEventHandler CreateSut() =>
        new(_client, _transformer, _statusCache, _httpClientFactory,
            NullLogger<RegistrationEventHandler>.Instance);

    [Fact]
    public async Task HandleAsync_NewAsset_ShouldCreateAsset()
    {
        var @event = CreateValidRegistrationEvent();

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(1);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);
        _client.CreateAssetAsync(default!, default!, default)
               .ReturnsForAnyArgs(new CreateAssetResponse
               {
                   Data = new AssetData { Id = "internal-001", AssetId = "Caterpillar-320-SN-9901" }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        await _client.Received(1).CreateAssetAsync(
            Arg.Is("proj-9001"), Arg.Any<CreateAssetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateAsset_ShouldThrowDuplicateAssetException()
    {
        var @event = CreateValidRegistrationEvent();

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(1);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs(new AssetSearchResult
               {
                   Id = "internal-001",
                   AssetId = "Caterpillar-320-SN-9901"
               });

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(@event, CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateAssetException>();
        await _client.DidNotReceive().CreateAssetAsync(
            Arg.Any<string>(), Arg.Any<CreateAssetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAssetCreated_ShouldUseStatusIdFromCache()
    {
        var @event = CreateValidRegistrationEvent();

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(42);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);
        _client.CreateAssetAsync(default!, default!, default)
               .ReturnsForAnyArgs(new CreateAssetResponse
               {
                   Data = new AssetData { Id = "internal-001", AssetId = "Caterpillar-320-SN-9901" }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        await _client.Received(1).CreateAssetAsync(
            Arg.Any<string>(),
            Arg.Is<CreateAssetRequest>(r => r.StatusId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithImageUrl_ShouldAttemptPhotoUpload()
    {
        var @event = CreateValidRegistrationEvent(imageUrl: "http://example.com/photo.jpg");

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(1);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);
        _client.CreateAssetAsync(default!, default!, default)
               .ReturnsForAnyArgs(new CreateAssetResponse
               {
                   Data = new AssetData { Id = "internal-001", AssetId = "Caterpillar-320-SN-9901" }
               });

        // Image download fails — photo upload failure must be non-fatal
        var fakeHttp = Substitute.For<HttpClient>();
        _httpClientFactory.CreateClient("ImageDownload").Returns(new HttpClient());

        var sut = CreateSut();
        // Should not throw even if photo download/upload fails
        var act = async () => await sut.HandleAsync(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_PhotoUploadFailure_ShouldNotFailAssetCreation()
    {
        var @event = CreateValidRegistrationEvent(imageUrl: "http://example.com/photo.jpg");

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(1);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);
        _client.CreateAssetAsync(default!, default!, default)
               .ReturnsForAnyArgs(new CreateAssetResponse
               {
                   Data = new AssetData { Id = "internal-001", AssetId = "Caterpillar-320-SN-9901" }
               });
        _client.UploadPhotoAsync(default!, default!, default!, default!, default)
               .ThrowsAsyncForAnyArgs(new HttpRequestException("upload failed"));

        _httpClientFactory.CreateClient("ImageDownload").Returns(new HttpClient());

        var sut = CreateSut();
        // Upload failure is non-fatal — should complete without throwing
        var act = async () => await sut.HandleAsync(@event, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_WithoutImageUrl_ShouldSkipPhotoUpload()
    {
        var @event = CreateValidRegistrationEvent(imageUrl: null);

        _statusCache.GetActiveStatusIdAsync(default).ReturnsForAnyArgs(1);
        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);
        _client.CreateAssetAsync(default!, default!, default)
               .ReturnsForAnyArgs(new CreateAssetResponse
               {
                   Data = new AssetData { Id = "internal-001", AssetId = "Caterpillar-320-SN-9901" }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        await _client.DidNotReceive().UploadPhotoAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static AssetRegistrationEvent CreateValidRegistrationEvent(string? imageUrl = null) => new()
    {
        EventType = EventTypes.AssetRegistration,
        EventId = "evt-a1b2c3",
        ProjectId = "proj-9001",
        SiteRef = "SITE-AU-042",
        Fields = new RegistrationFields
        {
            AssetName = "Caterpillar 320 Excavator",
            Make = "Caterpillar",
            Model = "320",
            SerialNumber = "SN-9901",
            YearMfg = "2021",
            RatePerHour = "220.00"
        },
        ImageUrl = imageUrl
    };
}
