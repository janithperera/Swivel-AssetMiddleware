using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.Events;
using Microsoft.Extensions.Logging;

namespace AssetMiddleware.Application.Handlers;

public sealed class RegistrationEventHandler : IEventHandler<AssetRegistrationEvent>
{
    private readonly IAssetHubClient _assetHubClient;
    private readonly IAssetTransformer _transformer;
    private readonly IAssetStatusCache _statusCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RegistrationEventHandler> _logger;

    public RegistrationEventHandler(
        IAssetHubClient assetHubClient,
        IAssetTransformer transformer,
        IAssetStatusCache statusCache,
        IHttpClientFactory httpClientFactory,
        ILogger<RegistrationEventHandler> logger)
    {
        _assetHubClient = assetHubClient;
        _transformer = transformer;
        _statusCache = statusCache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task HandleAsync(AssetRegistrationEvent @event, CancellationToken cancellationToken)
    {
        var activeStatusId = await _statusCache
            .GetActiveStatusIdAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = _transformer.TransformRegistration(@event, activeStatusId);

        _logger.LogInformation("Transformed registration event. AssetId: {AssetId}", request.AssetId);

        var existing = await _assetHubClient
            .SearchAssetByIdAsync(@event.ProjectId, request.AssetId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogError(
                "Duplicate asset detected. AssetId: {AssetId} already exists with Id: {ExistingId}",
                request.AssetId, existing.Id);
            throw new DuplicateAssetException(
                $"Asset '{request.AssetId}' already exists in project '{@event.ProjectId}'.");
        }

        var createResponse = await _assetHubClient
            .CreateAssetAsync(@event.ProjectId, request, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset created. AssetId: {AssetId}, Id: {Id}",
            createResponse.Data.AssetId, createResponse.Data.Id);

        if (!string.IsNullOrWhiteSpace(@event.ImageUrl))
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient("ImageDownload");
                await using var imageStream = await httpClient
                    .GetStreamAsync(@event.ImageUrl, cancellationToken)
                    .ConfigureAwait(false);

                var fileName = Path.GetFileName(new Uri(@event.ImageUrl).AbsolutePath);

                await _assetHubClient.UploadPhotoAsync(
                    @event.ProjectId, createResponse.Data.Id, imageStream, fileName, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("Photo uploaded for asset {AssetId}", request.AssetId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Photo upload failure is non-fatal — asset was already created successfully.
                _logger.LogWarning(ex,
                    "Failed to upload photo for asset {AssetId}. Continuing.", request.AssetId);
            }
        }
    }
}
