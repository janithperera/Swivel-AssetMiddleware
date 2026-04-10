namespace AssetMiddleware.Domain.Validation;

public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    public IReadOnlyList<ValidationError> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    public void AddError(string field, string message) =>
        _errors.Add(new ValidationError(field, message));

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            var details = string.Join("; ", _errors.Select(e => $"{e.Field}: {e.Message}"));
            throw new Exceptions.ValidationException($"Validation failed: {details}", _errors);
        }
    }
}
