namespace AssetMiddleware.Application.Configuration;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Used in local dev with the Service Bus Emulator.
    /// When set, takes precedence over FullyQualifiedNamespace + Managed Identity.
    /// Never set this in production — use FullyQualifiedNamespace and Managed Identity instead.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>Production: the fully qualified namespace, e.g. myns.servicebus.windows.net</summary>
    public string FullyQualifiedNamespace { get; init; } = default!;

    public string TopicName { get; init; } = default!;
    public string SubscriptionName { get; init; } = default!;

    /// <summary>Configurable without a code change.</summary>
    public int MaxConcurrentCalls { get; init; } = 5;
}
