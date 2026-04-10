namespace AssetMiddleware.Domain.Exceptions;

public sealed class DuplicateAssetException : Exception
{
    public DuplicateAssetException(string message) : base(message) { }
}
