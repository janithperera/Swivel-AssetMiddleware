using AssetMiddleware.Application.Transformers;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.Events;
using FluentAssertions;

namespace AssetMiddleware.Application.Tests.TransformerTests;

public class RegistrationTransformerTests
{
    private readonly AssetTransformer _sut = new();

    [Fact]
    public void TransformRegistration_ValidEvent_ShouldMapAllFields()
    {
        var @event = CreateValidRegistrationEvent();

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.AssetId.Should().Be("Caterpillar-320-SN-9901");
        result.Name.Should().Be("Caterpillar 320 Excavator");
        result.Make.Should().Be("Caterpillar");
        result.Model.Should().Be("320");
        result.SerialNumber.Should().Be("SN-9901");
        result.StatusId.Should().Be(1);
        result.YearMfg.Should().Be("2021");
        result.RatePerHour.Should().Be(220.00m);
        result.Ownership.Should().Be("Subcontracted");
        result.ProjectId.Should().Be("proj-9001");
    }

    [Fact]
    public void TransformRegistration_OwnershipField_ShouldAlwaysBeSubcontracted()
    {
        var @event = CreateValidRegistrationEvent();

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.Ownership.Should().Be("Subcontracted");
    }

    [Fact]
    public void TransformRegistration_AssetId_ShouldFollowMakeModelSerialFormat()
    {
        var @event = CreateValidRegistrationEvent();

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.AssetId.Should().Be($"{@event.Fields.Make}-{@event.Fields.Model}-{@event.Fields.SerialNumber}");
    }

    [Fact]
    public void TransformRegistration_MissingMake_ShouldThrowValidationException()
    {
        var @event = new AssetRegistrationEvent
        {
            EventType = EventTypes.AssetRegistration,
            EventId = "evt-a1b2c3",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            Fields = new RegistrationFields
            {
                AssetName = "Caterpillar 320 Excavator",
                Make = null!,
                Model = "320",
                SerialNumber = "SN-9901"
            }
        };

        var act = () => _sut.TransformRegistration(@event, activeStatusId: 1);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Any(err => err.Field == "fields.make"));
    }

    [Fact]
    public void TransformRegistration_MissingMultipleFields_ShouldCollectAllErrors()
    {
        var @event = new AssetRegistrationEvent
        {
            EventType = EventTypes.AssetRegistration,
            EventId = "evt-a1b2c3",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            Fields = new RegistrationFields
            {
                AssetName = "Caterpillar 320 Excavator",
                Make = null!,
                Model = null!,
                SerialNumber = null!
            }
        };

        var act = () => _sut.TransformRegistration(@event, activeStatusId: 1);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Count >= 3);
    }

    [Fact]
    public void TransformRegistration_InvalidRatePerHour_ShouldDegradeGracefully()
    {
        var @event = new AssetRegistrationEvent
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
                RatePerHour = "not-a-number"
            }
        };

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.RatePerHour.Should().BeNull();
    }

    [Fact]
    public void TransformRegistration_NullYearMfg_ShouldMapAsNull()
    {
        var @event = new AssetRegistrationEvent
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
                YearMfg = null
            }
        };

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.YearMfg.Should().BeNull();
    }

    [Fact]
    public void TransformRegistration_WhitespacePaddedFields_ShouldBeTrimmed()
    {
        var @event = new AssetRegistrationEvent
        {
            EventType = EventTypes.AssetRegistration,
            EventId = "evt-a1b2c3",
            ProjectId = " proj-9001 ",
            SiteRef = "SITE-AU-042",
            Fields = new RegistrationFields
            {
                AssetName = "  Caterpillar 320 Excavator  ",
                Make = " Caterpillar ",
                Model = " 320 ",
                SerialNumber = " SN-9901 "
            }
        };

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.Make.Should().Be("Caterpillar");
        result.Model.Should().Be("320");
        result.SerialNumber.Should().Be("SN-9901");
        result.Name.Should().Be("Caterpillar 320 Excavator");
        result.AssetId.Should().Be("Caterpillar-320-SN-9901");
    }

    [Fact]
    public void TransformRegistration_NullFields_ShouldThrowValidationExceptionImmediately()
    {
        var @event = new AssetRegistrationEvent
        {
            EventType = EventTypes.AssetRegistration,
            EventId = "evt-a1b2c3",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            Fields = null!
        };

        var act = () => _sut.TransformRegistration(@event, activeStatusId: 1);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void TransformRegistration_ActiveStatusId_ShouldBePassedThrough()
    {
        var @event = CreateValidRegistrationEvent();

        var result = _sut.TransformRegistration(@event, activeStatusId: 42);

        result.StatusId.Should().Be(42);
    }

    private static AssetRegistrationEvent CreateValidRegistrationEvent() => new()
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
            Category = "Earthmoving",
            Type = "Excavator",
            RatePerHour = "220.00",
            Supplier = "Hastings Deering Pty Ltd"
        },
        ImageUrl = "https://storage.example.com/assets/SN-9901.jpg"
    };
}
