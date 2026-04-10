namespace AssetMiddleware.Domain.Models.Events;

public sealed class AssetRegistrationEvent : FieldOpsEventBase
{
    public RegistrationFields Fields { get; init; } = default!;
    public string? ImageUrl { get; init; }
}

public sealed class RegistrationFields
{
    public string AssetName { get; init; } = default!;
    public string Make { get; init; } = default!;
    public string Model { get; init; } = default!;
    public string SerialNumber { get; init; } = default!;
    public string? YearMfg { get; init; }
    public string? Category { get; init; }
    public string? Type { get; init; }
    public string? RatePerHour { get; init; }
    public string? Supplier { get; init; }
}
