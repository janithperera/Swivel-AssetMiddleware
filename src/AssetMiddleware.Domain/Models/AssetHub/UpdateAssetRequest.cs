using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class UpdateAssetRequest
{
    [JsonPropertyName("onsite")]
    public bool? Onsite { get; init; }
}
