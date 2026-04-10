using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Infrastructure.ServiceBus;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AssetMiddleware.Infrastructure.Tests;

public class DeadLetterQueueProcessorTests
{
    private readonly ServiceBusClient _client = Substitute.For<ServiceBusClient>();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();
    private readonly ServiceBusSender _sender = Substitute.For<ServiceBusSender>();
    private readonly ILogger<DeadLetterQueueProcessor> _logger =
        Substitute.For<ILogger<DeadLetterQueueProcessor>>();
    private readonly DeadLetterQueueProcessor _sut;

    public DeadLetterQueueProcessorTests()
    {
        var options = Options.Create(new ServiceBusOptions
        {
            TopicName = "test-topic",
            SubscriptionName = "test-sub"
        });

        _client.CreateReceiver(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>())
            .Returns(_receiver);

        _client.CreateSender(Arg.Any<string>())
            .Returns(_sender);

        _sut = new DeadLetterQueueProcessor(_client, options, _logger);
    }

    private static ServiceBusReceivedMessage CreateDlqMessage(
        string body = "{\"eventType\":\"test\"}", string messageId = "msg-001",
        Dictionary<string, object>? properties = null)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            messageId: messageId,
            properties: properties);
    }

    // ── Empty queue ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_EmptyQueue_ReturnsZero()
    {
        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((ServiceBusReceivedMessage?)null);

        var result = await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        result.Should().Be(0);
        await _sender.DidNotReceive().SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
    }

    // ── Single message replay ─────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_OneMessage_SendsAndCompletesAndReturnsOne()
    {
        var message = CreateDlqMessage();

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(message, (ServiceBusReceivedMessage?)null);

        var result = await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        result.Should().Be(1);
        await _sender.Received(1).SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
        await _receiver.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
    }

    // ── Multiple messages ─────────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_ThreeMessages_ReplaysAll()
    {
        var msg1 = CreateDlqMessage(messageId: "msg-001");
        var msg2 = CreateDlqMessage(messageId: "msg-002");
        var msg3 = CreateDlqMessage(messageId: "msg-003");

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(msg1, msg2, msg3, null);

        var result = await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        result.Should().Be(3);
        await _sender.Received(3).SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
    }

    // ── MessageId preserved ───────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_PreservesMessageId()
    {
        var message = CreateDlqMessage(messageId: "original-id-42");

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(message, (ServiceBusReceivedMessage?)null);

        ServiceBusMessage? sentMessage = null;
        await _sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(m => sentMessage = m), Arg.Any<CancellationToken>());

        await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        sentMessage.Should().NotBeNull();
        sentMessage!.MessageId.Should().Be("original-id-42");
    }

    // ── ReplayCount tracking ──────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_FirstReplay_SetsReplayCountToOne()
    {
        var message = CreateDlqMessage();

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(message, (ServiceBusReceivedMessage?)null);

        ServiceBusMessage? sentMessage = null;
        await _sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(m => sentMessage = m), Arg.Any<CancellationToken>());

        await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        sentMessage!.ApplicationProperties["ReplayCount"].Should().Be(1);
    }

    [Fact]
    public async Task ReplayDeadLettersAsync_PreviouslyReplayed_IncrementsReplayCount()
    {
        var props = new Dictionary<string, object> { ["ReplayCount"] = 2 };
        var message = CreateDlqMessage(properties: props);

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(message, (ServiceBusReceivedMessage?)null);

        ServiceBusMessage? sentMessage = null;
        await _sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(m => sentMessage = m), Arg.Any<CancellationToken>());

        await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        sentMessage!.ApplicationProperties["ReplayCount"].Should().Be(3);
    }

    // ── Send failure → abandon ────────────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_SendFails_AbandonsMessageAndContinues()
    {
        var msg1 = CreateDlqMessage(messageId: "msg-fail");
        var msg2 = CreateDlqMessage(messageId: "msg-ok");

        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(msg1, msg2, null);

        var callCount = 0;
        _sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (callCount++ == 0) throw new ServiceBusException("send error", ServiceBusFailureReason.ServiceBusy);
                return Task.CompletedTask;
            });

        var result = await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        // First message failed → abandoned, second succeeded
        result.Should().Be(1);
        await _receiver.Received(1).AbandonMessageAsync(msg1, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await _receiver.Received(1).CompleteMessageAsync(msg2, Arg.Any<CancellationToken>());
    }

    // ── Receiver creates DLQ sub-queue ────────────────────────────────────

    [Fact]
    public async Task ReplayDeadLettersAsync_CreatesReceiverWithDeadLetterSubQueue()
    {
        _receiver.ReceiveMessageAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((ServiceBusReceivedMessage?)null);

        await _sut.ReplayDeadLettersAsync(CancellationToken.None);

        _client.Received(1).CreateReceiver(
            "test-topic", "test-sub",
            Arg.Is<ServiceBusReceiverOptions>(o => o.SubQueue == SubQueue.DeadLetter));
    }
}
