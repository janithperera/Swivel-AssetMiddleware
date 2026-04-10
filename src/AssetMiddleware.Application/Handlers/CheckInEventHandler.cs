using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.Events;
using Microsoft.Extensions.Logging;

namespace AssetMiddleware.Application.Handlers;

public sealed class CheckInEventHandler : IEventHandler<AssetCheckInEvent>
{
    private readonly IAssetHubClient _assetHubClient;
    private readonly IAssetTransformer _transformer;
    private readonly ILogger<CheckInEventHandler> _logger;

    public CheckInEventHandler(
        IAssetHubClient assetHubClient,
        IAssetTransformer transformer,
        ILogger<CheckInEventHandler> logger)
    {
        _assetHubClient = assetHubClient;
        _transformer = transformer;
        _logger = logger;
    }

    public async Task HandleAsync(AssetCheckInEvent @event, CancellationToken cancellationToken)
    {
        var (assetId, updateRequest) = _transformer.TransformCheckIn(@event);

        _logger.LogInformation(
            "Transformed check-in event. AssetId: {AssetId}, Onsite: {Onsite}",
            assetId, updateRequest.Onsite);

        var existing = await _assetHubClient
            .SearchAssetByIdAsync(@event.ProjectId, assetId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _logger.LogError(
                "Asset not found for check-in update. AssetId: {AssetId}, Project: {ProjectId}",
                assetId, @event.ProjectId);
            throw new AssetHubApiException(
                $"Asset '{assetId}' not found in project '{@event.ProjectId}'. " +
                "Cannot update onsite status for non-existent asset.");
        }

        var response = await _assetHubClient
            .UpdateAssetAsync(@event.ProjectId, existing.Id, updateRequest, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset onsite status updated. Id: {Id}, Onsite: {Onsite}",
            response.Data.Id, response.Data.Onsite);
    }
}
