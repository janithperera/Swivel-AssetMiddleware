using System.Globalization;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Domain.Models.AssetHub;
using AssetMiddleware.Domain.Models.Events;
using AssetMiddleware.Domain.Rules;
using AssetMiddleware.Domain.Validation;

namespace AssetMiddleware.Application.Transformers;

public sealed class AssetTransformer : IAssetTransformer
{
    public CreateAssetRequest TransformRegistration(AssetRegistrationEvent @event, int activeStatusId)
    {
        var validation = new ValidationResult();

        ValidateRequired(validation, "eventId", @event.EventId);
        ValidateRequired(validation, "projectId", @event.ProjectId);

        var fields = @event.Fields;
        if (fields is null)
        {
            validation.AddError("fields", "Fields object is required but was null");
            validation.ThrowIfInvalid();
        }

        ValidateRequired(validation, "fields.make", fields!.Make);
        ValidateRequired(validation, "fields.model", fields.Model);
        ValidateRequired(validation, "fields.serialNumber", fields.SerialNumber);
        ValidateRequired(validation, "fields.assetName", fields.AssetName);

        validation.ThrowIfInvalid();

        var assetId = AssetIdGenerator.Generate(fields.Make, fields.Model, fields.SerialNumber);

        decimal? ratePerHour = null;
        if (!string.IsNullOrWhiteSpace(fields.RatePerHour))
        {
            if (decimal.TryParse(fields.RatePerHour,
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
                ratePerHour = rate;
            // Degrade gracefully — don't crash on unparseable value
        }

        return new CreateAssetRequest
        {
            AssetId = assetId,
            Name = fields.AssetName.Trim(),
            Make = fields.Make.Trim(),
            Model = fields.Model.Trim(),
            StatusId = activeStatusId,
            SerialNumber = fields.SerialNumber.Trim(),
            YearMfg = fields.YearMfg?.Trim(),
            RatePerHour = ratePerHour,
            Ownership = "Subcontracted",   // Fixed business rule — never from payload
            ProjectId = @event.ProjectId
        };
    }

    public (string AssetId, UpdateAssetRequest Request) TransformCheckIn(AssetCheckInEvent @event)
    {
        var validation = new ValidationResult();

        ValidateRequired(validation, "eventId", @event.EventId);
        ValidateRequired(validation, "projectId", @event.ProjectId);
        ValidateRequired(validation, "make", @event.Make);
        ValidateRequired(validation, "model", @event.Model);
        ValidateRequired(validation, "serialNumber", @event.SerialNumber);

        validation.ThrowIfInvalid();

        // Throws ValidationException if checkInDate is null
        var onsite = OnsiteDerivation.Derive(@event.CheckInDate, @event.CheckOutDate);

        var assetId = AssetIdGenerator.Generate(@event.Make, @event.Model, @event.SerialNumber);

        return (assetId, new UpdateAssetRequest { Onsite = onsite });
    }

    private static void ValidateRequired(ValidationResult result, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            result.AddError(field, $"'{field}' is required but was null or empty");
    }
}
