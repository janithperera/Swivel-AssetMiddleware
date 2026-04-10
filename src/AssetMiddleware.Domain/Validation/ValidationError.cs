namespace AssetMiddleware.Domain.Validation;

public sealed record ValidationError(string Field, string Message);
