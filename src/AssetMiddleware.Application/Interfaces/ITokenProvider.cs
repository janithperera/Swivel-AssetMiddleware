namespace AssetMiddleware.Application.Interfaces;

public interface ITokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
    void InvalidateToken();
}
