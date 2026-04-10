using System.Text.Json.Serialization;

namespace AssetMiddleware.Domain.Models.AssetHub;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = default!;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = default!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }
}
