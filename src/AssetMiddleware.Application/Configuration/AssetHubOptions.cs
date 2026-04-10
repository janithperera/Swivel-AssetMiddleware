namespace AssetMiddleware.Application.Configuration;

public sealed class AssetHubOptions
{
    public const string SectionName = "AssetHub";

    public string BaseUrl { get; init; } = default!;
    public string TokenUrl { get; init; } = "/oauth/token";
    public string ClientId { get; init; } = default!;
    public string ClientSecret { get; init; } = default!;
    public string CompanyId { get; init; } = default!;

    /// <summary>
    /// Seconds before expiry to proactively refresh the token.
    /// Default: 400 s → refresh at 5000 s into a 5400 s token.
    /// </summary>
    public int TokenRefreshBufferSeconds { get; init; } = 400;
}
