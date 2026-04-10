using AssetMiddleware.Domain.Exceptions;

namespace AssetMiddleware.Domain.Rules;

public static class OnsiteDerivation
{
    /// <summary>
    /// Derives onsite status from check-in/check-out dates.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when checkInDate is null.</exception>
    public static bool Derive(DateTimeOffset? checkInDate, DateTimeOffset? checkOutDate)
    {
        if (checkInDate is null)
            throw new ValidationException("checkInDate is required but was null. Invalid event.");

        // checkOutDate is null → currently on site
        // checkOutDate is present → checked out
        return checkOutDate is null;
    }
}
