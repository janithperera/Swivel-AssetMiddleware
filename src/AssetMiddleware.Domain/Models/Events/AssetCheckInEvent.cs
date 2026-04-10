namespace AssetMiddleware.Domain.Models.Events;

public sealed class AssetCheckInEvent : FieldOpsEventBase
{
    public string SerialNumber { get; init; } = default!;
    public string Make { get; init; } = default!;
    public string Model { get; init; } = default!;
    public DateTimeOffset? CheckInDate { get; init; }
    public DateTimeOffset? CheckOutDate { get; init; }
}
