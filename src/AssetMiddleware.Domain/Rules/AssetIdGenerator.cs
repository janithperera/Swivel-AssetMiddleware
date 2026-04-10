namespace AssetMiddleware.Domain.Rules;

public static class AssetIdGenerator
{
    /// <summary>
    /// Generates Asset ID in format: Make-Model-SerialNumber.
    /// Preserves original casing. Hyphen separator.
    /// </summary>
    public static string Generate(string make, string model, string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        return $"{make.Trim()}-{model.Trim()}-{serialNumber.Trim()}";
    }
}
