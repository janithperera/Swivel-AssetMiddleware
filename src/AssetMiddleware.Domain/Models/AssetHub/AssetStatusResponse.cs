using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class AssetStatusResponse
{
    [JsonPropertyName("data")]
    public List<AssetStatus> Data { get; init; } = [];
}

public sealed class AssetStatus
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;
}
