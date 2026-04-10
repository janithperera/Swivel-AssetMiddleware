using AssetMiddleware.Domain.Rules;
using FluentAssertions;

namespace AssetMiddleware.Domain.Tests;

public class AssetIdGeneratorTests
{
    [Fact]
    public void Generate_ShouldConcatenateWithHyphens()
    {
        var result = AssetIdGenerator.Generate("Caterpillar", "320", "SN-9901");
        result.Should().Be("Caterpillar-320-SN-9901");
    }

    [Fact]
    public void Generate_ShouldPreserveOriginalCasing()
    {
        var result = AssetIdGenerator.Generate("KOMATSU", "pc200", "SN-1234");
        result.Should().Be("KOMATSU-pc200-SN-1234");
    }

    [Fact]
    public void Generate_ShouldTrimWhitespace()
    {
        var result = AssetIdGenerator.Generate(" Caterpillar ", " 320 ", " SN-9901 ");
        result.Should().Be("Caterpillar-320-SN-9901");
    }

    [Theory]
    [InlineData(null, "320", "SN-9901")]
    [InlineData("Caterpillar", null, "SN-9901")]
    [InlineData("Caterpillar", "320", null)]
    [InlineData("", "320", "SN-9901")]
    [InlineData("Caterpillar", "", "SN-9901")]
    [InlineData("Caterpillar", "320", "")]
    public void Generate_ShouldThrowOnMissingField(string? make, string? model, string? serial)
    {
        var act = () => AssetIdGenerator.Generate(make!, model!, serial!);
        act.Should().Throw<ArgumentException>();
    }
}
