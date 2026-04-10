using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Services;
using AssetMiddleware.Domain.Exceptions;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.ServiceBus;

public sealed class ServiceBusEventSubscriber : BackgroundService
{
    private readonly ServiceBusClient _client;
    // IServiceScopeFactory lets us create a fresh DI scope per message.
    // This avoids the captive dependency issue: BackgroundService is Singleton,
    // but EventRouter and its handlers are Scoped.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ServiceBusOptions> _options;
    private readonly ILogger<ServiceBusEventSubscriber> _logger;
    private ServiceBusProcessor? _processor;

    public ServiceBusEventSubscriber(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusEventSubscriber> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        _processor = _client.CreateProcessor(
            opts.TopicName,
            opts.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = opts.MaxConcurrentCalls,
                AutoCompleteMessages = false // We drive complete / abandon / dead-letter manually
            });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Service Bus subscriber started on {Topic}/{Subscription} (MaxConcurrentCalls={Max})",
            opts.TopicName, opts.SubscriptionName, opts.MaxConcurrentCalls);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown — fall through to StopAsync
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        // Fresh scope per message — EventRouter + handlers get new instances each time
        using var scope = _scopeFactory.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<EventRouter>();

        try
        {
            await router.RouteAsync(args.Message.Body.ToString(), args.CancellationToken)
                .ConfigureAwait(false);

            // Success — remove the message from the queue
            await args.CompleteMessageAsync(args.Message, args.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            // Domain validation failure — retrying would produce the same result.
            // Dead-letter immediately so a human can inspect and fix the source data.
            _logger.LogError(ex,
                "Validation failed for message {MessageId}. Dead-lettering.",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "ValidationFailed",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateAssetException ex)
        {
            // Asset already exists in AssetHub — retrying would always produce the same result.
            // Dead-letter so the DLQ replay endpoint can skip it after investigating.
            _logger.LogWarning(ex,
                "Duplicate asset detected for message {MessageId}. Dead-lettering.",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "DuplicateAsset",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
        catch (AssetHubApiException ex)
        {
            // Permanent API error — e.g. asset not found for a check-in update, or a null
            // response from AssetHub that indicates corrupt/unexpected state.
            // Retrying will not fix these. Dead-letter for investigation.
            _logger.LogError(ex,
                "Permanent AssetHub API error for message {MessageId}. Dead-lettering.",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "AssetHubApiError",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: args.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — abandon so the message is picked up after restart
            await args.AbandonMessageAsync(args.Message, cancellationToken: default)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Transient / unknown infrastructure failure (HTTP timeout, circuit open, etc.)
            // Abandon → Service Bus retry policy → auto-DLQ after MaxDeliveryCount
            _logger.LogError(ex,
                "Failed to process message {MessageId}. Abandoning for retry.",
                args.Message.MessageId);

            await args.AbandonMessageAsync(args.Message, cancellationToken: default)
                .ConfigureAwait(false);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        // ProcessErrorAsync must never throw — it would crash the processor.
        _logger.LogError(args.Exception,
            "Service Bus processor error. Source: {ErrorSource}, Entity: {EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await _processor.DisposeAsync().ConfigureAwait(false);
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
