using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class UpdateAssetResponse
{
    [JsonPropertyName("data")]
    public UpdateAssetData Data { get; init; } = default!;
}

public sealed class UpdateAssetData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("onsite")]
    public bool Onsite { get; init; }
}
