namespace AssetMiddleware.Domain.Models.Events;

public abstract class FieldOpsEventBase
{
    public string EventType { get; init; } = default!;
    public string EventId { get; init; } = default!;
    public string ProjectId { get; init; } = default!;
    public string SiteRef { get; init; } = default!;
}
