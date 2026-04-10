using System.Reflection;
using System.Text.Json;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Application.Services;
using AssetMiddleware.Domain.Constants;
using AssetMiddleware.Domain.Exceptions;
using AssetMiddleware.Domain.Models.Events;
using AssetMiddleware.Infrastructure.ServiceBus;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AssetMiddleware.Infrastructure.Tests;

public class ServiceBusEventSubscriberTests
{
    private readonly ServiceBusClient _client = Substitute.For<ServiceBusClient>();
    private readonly ServiceBusProcessor _processor = Substitute.For<ServiceBusProcessor>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IEventHandler<AssetRegistrationEvent> _registrationHandler =
        Substitute.For<IEventHandler<AssetRegistrationEvent>>();
    private readonly IEventHandler<AssetCheckInEvent> _checkInHandler =
        Substitute.For<IEventHandler<AssetCheckInEvent>>();
    private readonly ILogger<ServiceBusEventSubscriber> _logger =
        Substitute.For<ILogger<ServiceBusEventSubscriber>>();
    private readonly ServiceBusEventSubscriber _sut;

    public ServiceBusEventSubscriberTests()
    {
        var options = Options.Create(new ServiceBusOptions
        {
            TopicName = "test-topic",
            SubscriptionName = "test-sub",
            MaxConcurrentCalls = 3
        });

        _client.CreateProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_processor);
        _processor.StartProcessingAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _processor.StopProcessingAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Wire up DI scope chain: ScopeFactory → Scope → ServiceProvider → EventRouter
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        _scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        var router = new EventRouter(
            _registrationHandler,
            _checkInHandler,
            NullLogger<EventRouter>.Instance);

        serviceProvider.GetService(typeof(EventRouter)).Returns(router);

        _sut = new ServiceBusEventSubscriber(_client, _scopeFactory, options, _logger);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string RegistrationJson(string eventId = "evt-001") =>
        JsonSerializer.Serialize(new
        {
            eventType = EventTypes.AssetRegistration,
            eventId,
            projectId = "proj-001",
            siteRef = "SITE-A",
            fields = new { assetName = "Loader", make = "CAT", model = "950", serialNumber = "SN100" }
        });

    private ProcessMessageEventArgs CreateArgs(string messageBody, string messageId = "msg-001")
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(messageBody),
            messageId: messageId);

        var receiver = Substitute.For<ServiceBusReceiver>();

        var args = Substitute.For<ProcessMessageEventArgs>(message, receiver, CancellationToken.None);

        args.CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        args.DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        args.AbandonMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return args;
    }

    private async Task InvokeOnMessageAsync(ProcessMessageEventArgs args)
    {
        var method = typeof(ServiceBusEventSubscriber)
            .GetMethod("OnMessageAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("OnMessageAsync not found");

        await (Task)method.Invoke(_sut, [args])!;
    }

    // ── Structural tests ──────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_CreatesProcessorWithCorrectTopicAndSubscription()
    {
        using var cts = new CancellationTokenSource(500);
        await _sut.StartAsync(cts.Token);
        // Allow ExecuteAsync to run
        await Task.Delay(100);

        _client.Received(1).CreateProcessor(
            "test-topic", "test-sub",
            Arg.Any<ServiceBusProcessorOptions>());
    }

    [Fact]
    public async Task StartAsync_StartsProcessing()
    {
        using var cts = new CancellationTokenSource(500);
        await _sut.StartAsync(cts.Token);
        await Task.Delay(100);

        await _processor.Received(1).StartProcessingAsync(Arg.Any<CancellationToken>());
    }

    // ── Error categorisation — OnMessageAsync ─────────────────────────────

    [Fact]
    public async Task OnMessageAsync_SuccessfulProcessing_CompletesMessage()
    {
        var args = CreateArgs(RegistrationJson());

        await InvokeOnMessageAsync(args);

        await args.Received(1).CompleteMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnMessageAsync_ValidationException_DeadLettersWithValidationFailed()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ValidationException("Bad data"));

        var args = CreateArgs(RegistrationJson());
        await InvokeOnMessageAsync(args);

        await args.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            "ValidationFailed",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnMessageAsync_DuplicateAssetException_DeadLettersWithDuplicateAsset()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateAssetException("Already exists"));

        var args = CreateArgs(RegistrationJson());
        await InvokeOnMessageAsync(args);

        await args.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            "DuplicateAsset",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnMessageAsync_AssetHubApiException_DeadLettersWithAssetHubApiError()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AssetHubApiException("API broke"));

        var args = CreateArgs(RegistrationJson());
        await InvokeOnMessageAsync(args);

        await args.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            "AssetHubApiError",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnMessageAsync_OperationCancelled_AbandonsMessage()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var args = CreateArgs(RegistrationJson());
        await InvokeOnMessageAsync(args);

        await args.Received(1).AbandonMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            Arg.Any<IDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnMessageAsync_GenericException_AbandonsMessageForRetry()
    {
        _registrationHandler
            .HandleAsync(Arg.Any<AssetRegistrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("transient failure"));

        var args = CreateArgs(RegistrationJson());
        await InvokeOnMessageAsync(args);

        await args.Received(1).AbandonMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(),
            Arg.Any<IDictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    // ── Each message gets a fresh DI scope ────────────────────────────────

    [Fact]
    public async Task OnMessageAsync_CreatesFreshDiScope()
    {
        var args = CreateArgs(RegistrationJson());

        await InvokeOnMessageAsync(args);

        _scopeFactory.Received(1).CreateScope();
    }
}
