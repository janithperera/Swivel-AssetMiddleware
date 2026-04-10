using AssetMiddleware.Application.Handlers;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Application.Transformers;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.AssetHub;
using AssetMiddleware.Domain.Models.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AssetMiddleware.Application.Tests.HandlerTests;

public class CheckInEventHandlerTests
{
    private readonly IAssetHubClient _client = Substitute.For<IAssetHubClient>();
    private readonly IAssetTransformer _transformer = new AssetTransformer();

    private CheckInEventHandler CreateSut() =>
        new(_client, _transformer, NullLogger<CheckInEventHandler>.Instance);

    [Fact]
    public async Task HandleAsync_ExistingAsset_ShouldUpdateOnsiteStatus()
    {
        var @event = CreateCheckInEvent(checkOutDate: null);

        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs(new AssetSearchResult
               {
                   Id = "internal-001",
                   AssetId = "Caterpillar-320-SN-9901"
               });
        _client.UpdateAssetAsync(default!, default!, default!, default)
               .ReturnsForAnyArgs(new UpdateAssetResponse
               {
                   Data = new UpdateAssetData { Id = "internal-001", Onsite = true }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        await _client.Received(1).UpdateAssetAsync(
            Arg.Is("proj-9001"),
            Arg.Is("internal-001"),
            Arg.Is<UpdateAssetRequest>(r => r.Onsite == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CheckOut_ShouldUpdateOnsiteFalse()
    {
        var @event = CreateCheckInEvent(checkOutDate: DateTimeOffset.Now.AddHours(8));

        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs(new AssetSearchResult
               {
                   Id = "internal-001",
                   AssetId = "Caterpillar-320-SN-9901"
               });
        _client.UpdateAssetAsync(default!, default!, default!, default)
               .ReturnsForAnyArgs(new UpdateAssetResponse
               {
                   Data = new UpdateAssetData { Id = "internal-001", Onsite = false }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        await _client.Received(1).UpdateAssetAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<UpdateAssetRequest>(r => r.Onsite == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AssetNotFound_ShouldThrowAssetHubApiException()
    {
        var @event = CreateCheckInEvent(checkOutDate: null);

        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs((AssetSearchResult?)null);

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(@event, CancellationToken.None);

        await act.Should().ThrowAsync<AssetHubApiException>()
            .WithMessage("*not found*");

        await _client.DidNotReceive().UpdateAssetAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<UpdateAssetRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldSearchByDerivedAssetId()
    {
        var @event = CreateCheckInEvent(checkOutDate: null);

        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs(new AssetSearchResult
               {
                   Id = "internal-001",
                   AssetId = "Caterpillar-320-SN-9901"
               });
        _client.UpdateAssetAsync(default!, default!, default!, default)
               .ReturnsForAnyArgs(new UpdateAssetResponse
               {
                   Data = new UpdateAssetData { Id = "internal-001", Onsite = true }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        // Verifies that search was called with the correct derived asset ID
        await _client.Received(1).SearchAssetByIdAsync(
            Arg.Is("proj-9001"),
            Arg.Is("Caterpillar-320-SN-9901"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldUseInternalIdFromSearchForUpdate()
    {
        var @event = CreateCheckInEvent(checkOutDate: null);
        const string internalId = "db-uuid-123";

        _client.SearchAssetByIdAsync(default!, default!, default)
               .ReturnsForAnyArgs(new AssetSearchResult
               {
                   Id = internalId,
                   AssetId = "Caterpillar-320-SN-9901"
               });
        _client.UpdateAssetAsync(default!, default!, default!, default)
               .ReturnsForAnyArgs(new UpdateAssetResponse
               {
                   Data = new UpdateAssetData { Id = internalId, Onsite = true }
               });

        var sut = CreateSut();
        await sut.HandleAsync(@event, CancellationToken.None);

        // The internal DB id (not the assetId) must be used in the PATCH path
        await _client.Received(1).UpdateAssetAsync(
            Arg.Any<string>(),
            Arg.Is(internalId),
            Arg.Any<UpdateAssetRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static AssetCheckInEvent CreateCheckInEvent(DateTimeOffset? checkOutDate) => new()
    {
        EventType = EventTypes.AssetCheckIn,
        EventId = "evt-d4e5f6",
        ProjectId = "proj-9001",
        SiteRef = "SITE-AU-042",
        Make = "Caterpillar",
        Model = "320",
        SerialNumber = "SN-9901",
        CheckInDate = DateTimeOffset.Parse("2026-05-06T07:00:00+10:00"),
        CheckOutDate = checkOutDate
    };
}
