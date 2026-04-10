using AssetMiddleware.Application.Interfaces;

namespace AssetMiddleware.Api.Endpoints;

public static class DlqReplayEndpoint
{
    public static IEndpointRouteBuilder MapDlqReplayEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dlq/replay", async (
            IDeadLetterQueueProcessor processor,
            CancellationToken ct) =>
        {
            var count = await processor.ReplayDeadLettersAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { ReplayedCount = count, ReplayedAt = DateTimeOffset.UtcNow });
        })
        .WithName("ReplayDeadLetterQueue")
        .WithSummary("Replay all messages from the dead-letter queue")
        .WithDescription(
            "Reads every message from the DLQ and re-queues it to the main topic for re-processing. " +
            "Safe to call multiple times — the dedup check prevents duplicate asset creation.")
        .WithTags("Maintenance")
        .Produces<object>(StatusCodes.Status200OK);

        return app;
    }
}
