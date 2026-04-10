using AssetMiddleware.Domain.Validation;

namespace AssetMiddleware.Domain.Exceptions;

public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(string message, IReadOnlyList<ValidationError>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }
}
