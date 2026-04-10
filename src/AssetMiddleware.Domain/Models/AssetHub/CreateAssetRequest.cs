using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class CreateAssetRequest
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    [JsonPropertyName("make")]
    public string Make { get; init; } = default!;

    [JsonPropertyName("model")]
    public string Model { get; init; } = default!;

    [JsonPropertyName("statusId")]
    public int StatusId { get; init; }

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; init; } = default!;

    [JsonPropertyName("yearMfg")]
    public string? YearMfg { get; init; }

    [JsonPropertyName("ratePerHour")]
    public decimal? RatePerHour { get; init; }

    [JsonPropertyName("ownership")]
    public string Ownership { get; init; } = "Subcontracted";

    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = default!;
}
