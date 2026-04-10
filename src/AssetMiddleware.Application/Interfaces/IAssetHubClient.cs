using AssetMiddleware.Domain.Models.AssetHub;

namespace AssetMiddleware.Application.Interfaces;

public interface IAssetHubClient
{
    Task<int> GetActiveStatusIdAsync(CancellationToken ct);
    Task<AssetSearchResult?> SearchAssetByIdAsync(string projectId, string assetId, CancellationToken ct);
    Task<CreateAssetResponse> CreateAssetAsync(string projectId, CreateAssetRequest request, CancellationToken ct);
    Task<UpdateAssetResponse> UpdateAssetAsync(string projectId, string id, UpdateAssetRequest request, CancellationToken ct);
    Task UploadPhotoAsync(string projectId, string id, Stream photoStream, string fileName, CancellationToken ct);
}
