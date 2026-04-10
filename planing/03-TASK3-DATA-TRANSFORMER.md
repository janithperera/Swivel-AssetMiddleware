# Task 3 — Data Transformer (Field Mapping & Validation)

## Goal
Build a **pure transformation component** with zero external dependencies. It takes raw FieldOps events and produces validated, clean payloads for AssetHub. All business rules are enforced here.

---

## Domain Rules Summary (for quick reference)

| Rule | Detail |
|------|--------|
| **Asset ID format** | `{Make}-{Model}-{SerialNumber}` e.g. `Caterpillar-320-SN-9901`. Hyphen separator. Preserve original casing. |
| **Ownership** | Always `"Subcontracted"`. Never from payload. |
| **Onsite derivation** | `checkInDate` present + `checkOutDate` null → `true`. `checkOutDate` not null → `false`. `checkInDate` null → `ValidationException`. |
| **Dedup** | Handled by the handler (Task 1/2), NOT the transformer. Transformer just produces the payload. |

---

## Step-by-Step Implementation

### Step 3.1 — Asset ID Generator (Domain Layer)

**File:** `Domain/Rules/AssetIdGenerator.cs`

```csharp
public static class AssetIdGenerator
{
    /// <summary>
    /// Generates Asset ID in format: Make-Model-SerialNumber
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
```

**Why static?** Pure function, no state, no dependencies. Static is appropriate here.

---

### Step 3.2 — Onsite Derivation Rule (Domain Layer)

**File:** `Domain/Rules/OnsiteDerivation.cs`

```csharp
public static class OnsiteDerivation
{
    /// <summary>
    /// Derives onsite status from check-in/check-out dates.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when checkInDate is null (invalid event).</exception>
    public static bool Derive(DateTimeOffset? checkInDate, DateTimeOffset? checkOutDate)
    {
        if (checkInDate is null)
        {
            throw new ValidationException("checkInDate is required but was null. Invalid event.");
        }

        // checkOutDate is null → currently on site
        // checkOutDate is present → checked out
        return checkOutDate is null;
    }
}
```

---

### Step 3.3 — Validation Infrastructure (Domain Layer)

**File:** `Domain/Validation/ValidationError.cs`
```csharp
public sealed record ValidationError(string Field, string Message);
```

**File:** `Domain/Validation/ValidationResult.cs`
```csharp
public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    public IReadOnlyList<ValidationError> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    public void AddError(string field, string message)
    {
        _errors.Add(new ValidationError(field, message));
    }

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            var details = string.Join("; ", _errors.Select(e => $"{e.Field}: {e.Message}"));
            throw new ValidationException($"Validation failed: {details}", _errors);
        }
    }
}
```

**File:** `Domain/Exceptions/ValidationException.cs`
```csharp
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(string message, IReadOnlyList<ValidationError>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }
}
```

**This satisfies the optional requirement**: collect ALL validation errors before returning, rather than failing on the first one.

---

### Step 3.4 — Define Transformer Interface (Application Layer)

**File:** `Application/Interfaces/IAssetTransformer.cs`

```csharp
public interface IAssetTransformer
{
    CreateAssetRequest TransformRegistration(AssetRegistrationEvent @event, int activeStatusId);
    (string AssetId, UpdateAssetRequest Request) TransformCheckIn(AssetCheckInEvent @event);
}
```

---

### Step 3.5 — Implement the Transformer (Domain Layer)

**File:** `Domain/Transformers/AssetTransformer.cs`

This is the **core of Task 3**. Pure logic — no injected dependencies, no HTTP, no I/O.

```csharp
public sealed class AssetTransformer : IAssetTransformer
{
    /// <summary>
    /// Transforms an asset.registration.submitted event into a CreateAssetRequest.
    /// </summary>
    public CreateAssetRequest TransformRegistration(
        AssetRegistrationEvent @event, int activeStatusId)
    {
        // ── Validate ───────────────────────────────────────────────
        var validation = new ValidationResult();

        ValidateRequired(validation, "eventId", @event.EventId);
        ValidateRequired(validation, "projectId", @event.ProjectId);

        var fields = @event.Fields;
        if (fields is null)
        {
            validation.AddError("fields", "Fields object is required but was null");
            validation.ThrowIfInvalid(); // Can't continue without fields
        }

        ValidateRequired(validation, "fields.make", fields!.Make);
        ValidateRequired(validation, "fields.model", fields.Model);
        ValidateRequired(validation, "fields.serialNumber", fields.SerialNumber);
        ValidateRequired(validation, "fields.assetName", fields.AssetName);

        // Throw ALL collected errors at once
        validation.ThrowIfInvalid();

        // ── Generate Asset ID ──────────────────────────────────────
        var assetId = AssetIdGenerator.Generate(
            fields.Make, fields.Model, fields.SerialNumber);

        // ── Parse optional numeric fields defensively ──────────────
        decimal? ratePerHour = null;
        if (!string.IsNullOrWhiteSpace(fields.RatePerHour))
        {
            if (decimal.TryParse(fields.RatePerHour, 
                NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            {
                ratePerHour = rate;
            }
            // If parsing fails, degrade gracefully — don't crash
        }

        // ── Map to target payload ──────────────────────────────────
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
            Ownership = "Subcontracted", // ALWAYS — business rule
            ProjectId = @event.ProjectId.Trim()
        };
    }

    /// <summary>
    /// Transforms an asset.checkin.updated event into an UpdateAssetRequest.
    /// Also returns the generated AssetId needed for the dedup/lookup step.
    /// </summary>
    public (string AssetId, UpdateAssetRequest Request) TransformCheckIn(
        AssetCheckInEvent @event)
    {
        // ── Validate ───────────────────────────────────────────────
        var validation = new ValidationResult();

        ValidateRequired(validation, "eventId", @event.EventId);
        ValidateRequired(validation, "projectId", @event.ProjectId);
        ValidateRequired(validation, "make", @event.Make);
        ValidateRequired(validation, "model", @event.Model);
        ValidateRequired(validation, "serialNumber", @event.SerialNumber);

        validation.ThrowIfInvalid();

        // ── Generate Asset ID ──────────────────────────────────────
        var assetId = AssetIdGenerator.Generate(
            @event.Make, @event.Model, @event.SerialNumber);

        // ── Derive onsite status ───────────────────────────────────
        // This throws ValidationException if checkInDate is null
        var onsite = OnsiteDerivation.Derive(@event.CheckInDate, @event.CheckOutDate);

        // ── Map to target payload ──────────────────────────────────
        var request = new UpdateAssetRequest
        {
            Onsite = onsite
        };

        return (assetId, request);
    }

    // ── Private helpers ────────────────────────────────────────────

    private static void ValidateRequired(
        ValidationResult validation, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            validation.AddError(fieldName, $"{fieldName} is required but was null or empty");
        }
    }
}
```

**Key design decisions:**

| Decision | Why |
|----------|-----|
| `ValidationResult` collects all errors | Optional requirement: don't fail on first error. Collect all, then throw. |
| `decimal.TryParse` for `ratePerHour` | Defensive — source sends string `"220.00"`. Graceful degradation on parse failure. |
| `InvariantCulture` for parsing | Prevents locale-dependent bugs (e.g., `220,00` vs `220.00`). |
| `Trim()` on all string fields | Normalise consistently — handles trailing spaces. |
| `Ownership = "Subcontracted"` hardcoded | Business rule — never from payload. This is the ONLY place it's set. |
| Returns `(AssetId, Request)` tuple for check-in | Handler needs AssetId to look up the existing asset before patching. |
| No external dependencies | Pure transformation — testable with just `new AssetTransformer()`. |

---

### Step 3.6 — Unit Tests (Domain Tests)

**File:** `tests/AssetMiddleware.Domain.Tests/AssetIdGeneratorTests.cs`

```csharp
public class AssetIdGeneratorTests
{
    [Fact]
    public void Generate_ShouldConcatenateWithHyphens()
    {
        var result = AssetIdGenerator.Generate("Caterpillar", "320", "SN-9901");
        result.Should().Be("Caterpillar-320-SN-9901");
    }

    [Fact]
    public void Generate_ShouldPreserveOriginalCasing()
    {
        var result = AssetIdGenerator.Generate("KOMATSU", "pc200", "SN-1234");
        result.Should().Be("KOMATSU-pc200-SN-1234");
    }

    [Fact]
    public void Generate_ShouldTrimWhitespace()
    {
        var result = AssetIdGenerator.Generate(" Caterpillar ", " 320 ", " SN-9901 ");
        result.Should().Be("Caterpillar-320-SN-9901");
    }

    [Theory]
    [InlineData(null, "320", "SN-9901")]
    [InlineData("Caterpillar", null, "SN-9901")]
    [InlineData("Caterpillar", "320", null)]
    [InlineData("", "320", "SN-9901")]
    [InlineData("Caterpillar", "", "SN-9901")]
    public void Generate_ShouldThrowOnMissingField(string? make, string? model, string? serial)
    {
        var act = () => AssetIdGenerator.Generate(make!, model!, serial!);
        act.Should().Throw<ArgumentException>();
    }
}
```

**File:** `tests/AssetMiddleware.Domain.Tests/OnsiteDerivationTests.cs`

```csharp
public class OnsiteDerivationTests
{
    [Fact]
    public void Derive_CheckInPresent_CheckOutNull_ShouldReturnTrue()
    {
        var result = OnsiteDerivation.Derive(
            DateTimeOffset.Now, null);
        result.Should().BeTrue();
    }

    [Fact]
    public void Derive_CheckInPresent_CheckOutPresent_ShouldReturnFalse()
    {
        var result = OnsiteDerivation.Derive(
            DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));
        result.Should().BeFalse();
    }

    [Fact]
    public void Derive_CheckInNull_ShouldThrowValidationException()
    {
        var act = () => OnsiteDerivation.Derive(null, null);
        act.Should().Throw<ValidationException>()
            .WithMessage("*checkInDate*");
    }
}
```

**File:** `tests/AssetMiddleware.Domain.Tests/TransformerTests/RegistrationTransformerTests.cs`

```csharp
public class RegistrationTransformerTests
{
    private readonly AssetTransformer _sut = new();

    [Fact]
    public void TransformRegistration_ValidEvent_ShouldMapAllFields()
    {
        var @event = CreateValidRegistrationEvent();
        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.AssetId.Should().Be("Caterpillar-320-SN-9901");
        result.Name.Should().Be("Caterpillar 320 Excavator");
        result.Make.Should().Be("Caterpillar");
        result.Model.Should().Be("320");
        result.SerialNumber.Should().Be("SN-9901");
        result.StatusId.Should().Be(1);
        result.YearMfg.Should().Be("2021");
        result.RatePerHour.Should().Be(220.00m);
        result.Ownership.Should().Be("Subcontracted");
        result.ProjectId.Should().Be("proj-9001");
    }

    [Fact]
    public void TransformRegistration_OwnershipField_ShouldAlwaysBeSubcontracted()
    {
        // Even if someone tries to sneak in a different ownership value,
        // the transformer ignores it — hardcoded to "Subcontracted"
        var @event = CreateValidRegistrationEvent();
        var result = _sut.TransformRegistration(@event, activeStatusId: 1);

        result.Ownership.Should().Be("Subcontracted");
    }

    [Fact]
    public void TransformRegistration_MissingMake_ShouldThrowValidationException()
    {
        var @event = CreateValidRegistrationEvent();
        @event.Fields.Make = null!;

        var act = () => _sut.TransformRegistration(@event, activeStatusId: 1);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Any(err => err.Field == "fields.make"));
    }

    [Fact]
    public void TransformRegistration_MissingMultipleFields_ShouldCollectAllErrors()
    {
        var @event = CreateValidRegistrationEvent();
        @event.Fields.Make = null!;
        @event.Fields.Model = null!;
        @event.Fields.SerialNumber = null!;

        var act = () => _sut.TransformRegistration(@event, activeStatusId: 1);
        act.Should().Throw<ValidationException>()
            .Where(e => e.Errors.Count >= 3);
    }

    [Fact]
    public void TransformRegistration_InvalidRatePerHour_ShouldDegradeGracefully()
    {
        var @event = CreateValidRegistrationEvent();
        @event.Fields.RatePerHour = "not-a-number";

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);
        result.RatePerHour.Should().BeNull(); // Graceful degradation
    }

    [Fact]
    public void TransformRegistration_NullYearMfg_ShouldMapAsNull()
    {
        var @event = CreateValidRegistrationEvent();
        @event.Fields.YearMfg = null;

        var result = _sut.TransformRegistration(@event, activeStatusId: 1);
        result.YearMfg.Should().BeNull();
    }

    private static AssetRegistrationEvent CreateValidRegistrationEvent() => new()
    {
        EventType = EventTypes.AssetRegistration,
        EventId = "evt-a1b2c3",
        ProjectId = "proj-9001",
        SiteRef = "SITE-AU-042",
        Fields = new RegistrationFields
        {
            AssetName = "Caterpillar 320 Excavator",
            Make = "Caterpillar",
            Model = "320",
            SerialNumber = "SN-9901",
            YearMfg = "2021",
            Category = "Earthmoving",
            Type = "Excavator",
            RatePerHour = "220.00",
            Supplier = "Hastings Deering Pty Ltd"
        },
        ImageUrl = "https://storage.example.com/assets/SN-9901.jpg"
    };
}
```

**File:** `tests/AssetMiddleware.Domain.Tests/TransformerTests/CheckInTransformerTests.cs`

```csharp
public class CheckInTransformerTests
{
    private readonly AssetTransformer _sut = new();

    [Fact]
    public void TransformCheckIn_CheckedIn_ShouldReturnOnsiteTrue()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = DateTimeOffset.Parse("2026-05-06T07:00:00+10:00"),
            CheckOutDate = null
        };

        var (assetId, request) = _sut.TransformCheckIn(@event);

        assetId.Should().Be("Caterpillar-320-SN-9901");
        request.Onsite.Should().BeTrue();
    }

    [Fact]
    public void TransformCheckIn_CheckedOut_ShouldReturnOnsiteFalse()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = DateTimeOffset.Parse("2026-05-06T07:00:00+10:00"),
            CheckOutDate = DateTimeOffset.Parse("2026-05-06T15:00:00+10:00")
        };

        var (_, request) = _sut.TransformCheckIn(@event);
        request.Onsite.Should().BeFalse();
    }

    [Fact]
    public void TransformCheckIn_NullCheckInDate_ShouldThrowValidationException()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-9901",
            Make = "Caterpillar",
            Model = "320",
            CheckInDate = null,
            CheckOutDate = null
        };

        var act = () => _sut.TransformCheckIn(@event);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void TransformCheckIn_GeneratesCorrectAssetId()
    {
        var @event = new AssetCheckInEvent
        {
            EventType = EventTypes.AssetCheckIn,
            EventId = "evt-d4e5f6",
            ProjectId = "proj-9001",
            SiteRef = "SITE-AU-042",
            SerialNumber = "SN-5555",
            Make = "Komatsu",
            Model = "PC200",
            CheckInDate = DateTimeOffset.Now,
            CheckOutDate = null
        };

        var (assetId, _) = _sut.TransformCheckIn(@event);
        assetId.Should().Be("Komatsu-PC200-SN-5555");
    }
}
```

---

## Checklist for Task 3

- [ ] `AssetIdGenerator.Generate()` — `Make-Model-SerialNumber`, original casing, trimmed
- [ ] `OnsiteDerivation.Derive()` — `checkInDate` null → throw, `checkOutDate` null → true, else → false
- [ ] `ValidationResult` collects all errors before throwing (optional requirement)
- [ ] `ValidationException` carries list of `ValidationError`
- [ ] `AssetTransformer.TransformRegistration()` — validates, generates AssetId, maps all fields
- [ ] `Ownership` hardcoded to `"Subcontracted"` — never from payload
- [ ] `ratePerHour` parsed with `TryParse` + `InvariantCulture` — graceful degradation
- [ ] `YearMfg`, `Category`, `Supplier` handled as optional (null-safe)
- [ ] `Trim()` applied to all string fields
- [ ] `AssetTransformer.TransformCheckIn()` — validates, generates AssetId, derives onsite
- [ ] Returns tuple `(AssetId, UpdateAssetRequest)` for handler use
- [ ] No external dependencies — pure transformation logic
- [ ] Unit tests for `AssetIdGenerator`
- [ ] Unit tests for `OnsiteDerivation`
- [ ] Unit tests for `TransformRegistration` — happy path, missing fields, graceful degradation
- [ ] Unit tests for `TransformCheckIn` — checked in, checked out, null checkInDate

---

## Common Pitfalls to Avoid

1. **Using `CultureInfo.CurrentCulture` for parsing** — breaks on different machines. Always `InvariantCulture`.
2. **Throwing on first validation error** — collect all errors. Senior expectation.
3. **Reading ownership from the event** — must be hardcoded. Business rule.
4. **Not trimming string values** — stale whitespace propagates to the target system.
5. **Forgetting to generate the AssetId for check-in events** — it's needed for the dedup/lookup step.
