using AssetMiddleware.Domain.Models.Events;

namespace AssetMiddleware.Application.Interfaces;

public interface IEventHandler<in TEvent> where TEvent : FieldOpsEventBase
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
