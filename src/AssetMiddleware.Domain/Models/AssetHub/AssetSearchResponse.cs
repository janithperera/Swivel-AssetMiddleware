using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class AssetSearchResponse
{
    [JsonPropertyName("data")]
    public List<AssetSearchResult> Data { get; init; } = [];
}

public sealed class AssetSearchResult
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = default!;

    [JsonPropertyName("onsite")]
    public bool Onsite { get; init; }
}
