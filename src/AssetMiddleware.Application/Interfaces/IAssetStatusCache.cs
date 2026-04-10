namespace AssetMiddleware.Application.Interfaces;

public interface IAssetStatusCache
{
    Task<int> GetActiveStatusIdAsync(CancellationToken ct);
}
