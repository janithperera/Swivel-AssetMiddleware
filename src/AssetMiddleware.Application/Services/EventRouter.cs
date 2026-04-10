using System.Text.Json;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Models.Events;
using Microsoft.Extensions.Logging;

namespace AssetMiddleware.Application.Services;

public sealed class EventRouter
{
    private readonly IEventHandler<AssetRegistrationEvent> _registrationHandler;
    private readonly IEventHandler<AssetCheckInEvent> _checkInHandler;
    private readonly ILogger<EventRouter> _logger;

    public EventRouter(
        IEventHandler<AssetRegistrationEvent> registrationHandler,
        IEventHandler<AssetCheckInEvent> checkInHandler,
        ILogger<EventRouter> logger)
    {
        _registrationHandler = registrationHandler;
        _checkInHandler = checkInHandler;
        _logger = logger;
    }

    public async Task RouteAsync(string messageBody, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(messageBody);
        var eventType = doc.RootElement.GetProperty("eventType").GetString();
        var eventId = doc.RootElement.GetProperty("eventId").GetString();

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = eventId,
            ["EventType"] = eventType
        }))
        {
            _logger.LogInformation("Received event {EventType} with ID {EventId}", eventType, eventId);

            switch (eventType)
            {
                case EventTypes.AssetRegistration:
                    var regEvent = JsonSerializer.Deserialize<AssetRegistrationEvent>(messageBody)!;
                    await _registrationHandler.HandleAsync(regEvent, ct).ConfigureAwait(false);
                    break;

                case EventTypes.AssetCheckIn:
                    var checkInEvent = JsonSerializer.Deserialize<AssetCheckInEvent>(messageBody)!;
                    await _checkInHandler.HandleAsync(checkInEvent, ct).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning("Unknown event type: {EventType}. Completing message.", eventType);
                    break;
            }

            _logger.LogInformation("Successfully processed event {EventId}", eventId);
        }
    }
}
