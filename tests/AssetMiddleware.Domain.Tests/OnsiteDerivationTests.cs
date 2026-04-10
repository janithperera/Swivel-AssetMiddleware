using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Rules;
using FluentAssertions;

namespace AssetMiddleware.Domain.Tests;

public class OnsiteDerivationTests
{
    [Fact]
    public void Derive_CheckInPresent_CheckOutNull_ShouldReturnTrue()
    {
        var result = OnsiteDerivation.Derive(DateTimeOffset.Now, null);
        result.Should().BeTrue();
    }

    [Fact]
    public void Derive_CheckInPresent_CheckOutPresent_ShouldReturnFalse()
    {
        var result = OnsiteDerivation.Derive(
            DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));
        result.Should().BeFalse();
    }

    [Fact]
    public void Derive_CheckInNull_ShouldThrowValidationException()
    {
        var act = () => OnsiteDerivation.Derive(null, null);
        act.Should().Throw<ValidationException>()
            .WithMessage("*checkInDate*");
    }

    [Fact]
    public void Derive_CheckInNull_WithCheckOutPresent_ShouldStillThrow()
    {
        var act = () => OnsiteDerivation.Derive(null, DateTimeOffset.Now);
        act.Should().Throw<ValidationException>();
    }
}
