using AssetMiddleware.Domain.Models.AssetHub;
using AssetMiddleware.Domain.Models.Events;

namespace AssetMiddleware.Application.Interfaces;

public interface IAssetTransformer
{
    CreateAssetRequest TransformRegistration(AssetRegistrationEvent @event, int activeStatusId);
    (string AssetId, UpdateAssetRequest Request) TransformCheckIn(AssetCheckInEvent @event);
}
