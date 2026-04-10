using AssetMiddleware.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AssetMiddleware.Infrastructure.Caching;

public sealed class AssetStatusCache : IAssetStatusCache
{
    private readonly IAssetHubClient _client;
    private readonly ILogger<AssetStatusCache> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private int? _activeStatusId;

    public AssetStatusCache(IAssetHubClient client, ILogger<AssetStatusCache> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> GetActiveStatusIdAsync(CancellationToken ct)
    {
        if (_activeStatusId.HasValue)
            return _activeStatusId.Value;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_activeStatusId.HasValue)
                return _activeStatusId.Value;

            _logger.LogInformation("Fetching Active status ID from AssetHub...");
            _activeStatusId = await _client.GetActiveStatusIdAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Cached Active status ID: {StatusId}", _activeStatusId);

            return _activeStatusId.Value;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
