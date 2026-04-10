using System.Text.Json;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Application.Services;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Models.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AssetMiddleware.Application.Tests;

public class EventRouterTests
{
    private readonly IEventHandler<AssetRegistrationEvent> _registrationHandler =
        Substitute.For<IEventHandler<AssetRegistrationEvent>>();

    private readonly IEventHandler<AssetCheckInEvent> _checkInHandler =
        Substitute.For<IEventHandler<AssetCheckInEvent>>();

    private readonly ILogger<EventRouter> _logger =
        Substitute.For<ILogger<EventRouter>>();

    private readonly EventRouter _sut;

    public EventRouterTests()
    {
        _sut = new EventRouter(_registrationHandler, _checkInHandler, _logger);
    }

    private static string BuildMessage(string eventType, string eventId = "evt-001") =>
        JsonSerializer.Serialize(new
        {
            eventType,
            eventId,
            projectId = "proj-001",
            siteRef = "SITE-A",
            fields = new
            {
                assetName = "Excavator",
                make = "CAT",
                model = "320",
                serialNumber = "SN001"
            }
        });

    // ── Registration routing ──────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_RegistrationEvent_DispatchesToRegistrationHandler()
    {
        var body = BuildMessage(EventTypes.AssetRegistration);

        await _sut.RouteAsync(body, CancellationToken.None);

        await _registrationHandler.Received(1)
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>());
        await _checkInHandler.DidNotReceive()
            .HandleAsync(Arg.Any<AssetCheckInEvent>(), Arg.Any<CancellationToken>());
    }

    // ── Check-in routing ──────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_CheckInEvent_DispatchesToCheckInHandler()
    {
        var body = JsonSerializer.Serialize(new
        {
            eventType = EventTypes.AssetCheckIn,
            eventId = "evt-002",
            projectId = "proj-001",
            siteRef = "SITE-A",
            serialNumber = "SN001",
            make = "CAT",
            model = "320"
        });

        await _sut.RouteAsync(body, CancellationToken.None);

        await _checkInHandler.Received(1)
            .HandleAsync(Arg.Any<AssetCheckInEvent>(), Arg.Any<CancellationToken>());
        await _registrationHandler.DidNotReceive()
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>());
    }

    // ── Unknown event type ────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_UnknownEventType_CompletesWithoutCallingAnyHandler()
    {
        var body = BuildMessage("asset.unknown.event");

        await _sut.RouteAsync(body, CancellationToken.None);

        await _registrationHandler.DidNotReceive()
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>());
        await _checkInHandler.DidNotReceive()
            .HandleAsync(Arg.Any<AssetCheckInEvent>(), Arg.Any<CancellationToken>());
    }

    // ── JSON deserialization ──────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_RegistrationEvent_DeserializesEvent()
    {
        var body = BuildMessage(EventTypes.AssetRegistration, "evt-100");

        await _sut.RouteAsync(body, CancellationToken.None);

        await _registrationHandler.Received(1).HandleAsync(
            Arg.Is<AssetRegistrationEvent>(e => e != null),
            Arg.Any<CancellationToken>());
    }

    // ── Invalid JSON ──────────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_InvalidJson_ThrowsJsonException()
    {
        var act = async () => await _sut.RouteAsync("NOT JSON", CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>();
    }

    // ── Missing eventType property ────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_MissingEventType_ThrowsKeyNotFoundException()
    {
        var body = JsonSerializer.Serialize(new { eventId = "evt-001", projectId = "proj-001" });

        var act = async () => await _sut.RouteAsync(body, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Handler exception propagates ──────────────────────────────────────

    [Fact]
    public async Task RouteAsync_HandlerThrows_ExceptionPropagates()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler error")));

        var body = BuildMessage(EventTypes.AssetRegistration);
        var act = async () => await _sut.RouteAsync(body, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler error");
    }
}
