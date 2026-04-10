namespace AssetMiddleware.Application.Interfaces;

public interface IDeadLetterQueueProcessor
{
    Task<int> ReplayDeadLettersAsync(CancellationToken ct);
}
