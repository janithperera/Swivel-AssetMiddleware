using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class CreateAssetResponse
{
    [JsonPropertyName("data")]
    public AssetData Data { get; init; } = default!;
}

public sealed class AssetData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;
}
