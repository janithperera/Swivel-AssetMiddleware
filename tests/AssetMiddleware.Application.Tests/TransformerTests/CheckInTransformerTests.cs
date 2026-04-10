using AssetMiddleware.Application.Transformers;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.Events;
using FluentAssertions;

namespace AssetMiddleware.Application.Tests.TransformerTests;

public class CheckInTransformerTests
{
    private readonly AssetTransformer _sut = new();

    [Fact]
    public void TransformCheckIn_CheckedIn_ShouldReturnOnsiteTrue()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = DateTimeOffset.Parse("2026-05-06T07:00:00+10:00"),
            CheckOutDate = null
        };

        var (assetId, request) = _sut.TransformCheckIn(@event);

        assetId.Should().Be("Caterpillar-320-SN-9901");
        request.Onsite.Should().BeTrue();
    }

    [Fact]
    public void TransformCheckIn_CheckedOut_ShouldReturnOnsiteFalse()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = DateTimeOffset.Parse("2026-05-06T07:00:00+10:00"),
            CheckOutDate = DateTimeOffset.Parse("2026-05-06T15:00:00+10:00")
        };

        var (_, request) = _sut.TransformCheckIn(@event);

        request.Onsite.Should().BeFalse();
    }

    [Fact]
    public void TransformCheckIn_NullCheckInDate_ShouldThrowValidationException()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = null,
            CheckOutDate = null
        };

        var act = () => _sut.TransformCheckIn(@event);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void TransformCheckIn_GeneratesCorrectAssetId()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-5555",
            Make = "Komatsu",
            Model = "PC200",
            CheckInDate = DateTimeOffset.Now,
            CheckOutDate = null
        };

        var (assetId, _) = _sut.TransformCheckIn(@event);

        assetId.Should().Be("Komatsu-PC200-SN-5555");
    }

    [Fact]
    public void TransformCheckIn_MissingMake_ShouldThrowValidationException()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = null!,
            Model = "320",
            CheckInDate = DateTimeOffset.Now,
            CheckOutDate = null
        };

        var act = () => _sut.TransformCheckIn(@event);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Any(err => err.Field == "make"));
    }

    [Fact]
    public void TransformCheckIn_MissingSerialNumber_ShouldThrowValidationException()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = null!,
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = DateTimeOffset.Now,
            CheckOutDate = null
        };

        var act = () => _sut.TransformCheckIn(@event);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Any(err => err.Field == "serialNumber"));
    }

    [Fact]
    public void TransformCheckIn_PreservesOriginalCasingInAssetId()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "sn-lower",
            Make = "KOMATSU",
            Model = "pc200",
            CheckInDate = DateTimeOffset.Now,
            CheckOutDate = null
        };

        var (assetId, _) = _sut.TransformCheckIn(@event);

        assetId.Should().Be("KOMATSU-pc200-sn-lower");
    }
}
