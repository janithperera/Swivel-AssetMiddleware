namespace AssetMiddleware.Domain.Exceptions;

public sealed class AssetHubApiException : Exception
{
    public AssetHubApiException(string message) : base(message) { }
    public AssetHubApiException(string message, Exception inner) : base(message, inner) { }
}
