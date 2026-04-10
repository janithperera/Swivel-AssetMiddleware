using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Interfaces;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetMiddleware.Infrastructure.ServiceBus;

public sealed class DeadLetterQueueProcessor : IDeadLetterQueueProcessor
{
    private readonly ServiceBusClient _client;
    private readonly IOptions<ServiceBusOptions> _options;
    private readonly ILogger<DeadLetterQueueProcessor> _logger;

    public DeadLetterQueueProcessor(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<DeadLetterQueueProcessor> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ReplayDeadLettersAsync(CancellationToken ct)
    {
        var opts = _options.Value;

        await using var receiver = _client.CreateReceiver(
            opts.TopicName,
            opts.SubscriptionName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        await using var sender = _client.CreateSender(opts.TopicName);

        _logger.LogInformation(
            "Starting DLQ replay for {Topic}/{Subscription}", opts.TopicName, opts.SubscriptionName);

        var replayedCount = 0;

        while (!ct.IsCancellationRequested)
        {
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), ct)
                .ConfigureAwait(false);

            if (message is null)
                break;

            try
            {
                var requeued = new ServiceBusMessage(message.Body)
                {
                    ContentType = message.ContentType,
                    Subject = message.Subject,
                    MessageId = message.MessageId   // Same ID — idempotency
                };

                // Copy application properties
                foreach (var prop in message.ApplicationProperties)
                    requeued.ApplicationProperties[prop.Key] = prop.Value;

                // Track how many times this message has been replayed
                var replayCount = message.ApplicationProperties
                    .TryGetValue("ReplayCount", out var existing) ? (int)existing + 1 : 1;
                requeued.ApplicationProperties["ReplayCount"] = replayCount;

                _logger.LogInformation(
                    "Replaying DLQ message {MessageId} (replay #{ReplayCount}). " +
                    "Dead-letter reason: {Reason}",
                    message.MessageId, replayCount, message.DeadLetterReason);

                await sender.SendMessageAsync(requeued, ct).ConfigureAwait(false);
                await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false);

                replayedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to replay DLQ message {MessageId}", message.MessageId);
                await receiver.AbandonMessageAsync(message, cancellationToken: default).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("DLQ replay complete. Replayed {Count} message(s).", replayedCount);
        return replayedCount;
    }
}
