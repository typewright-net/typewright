# Domain Model Validation

- Fully validated domain models that make representing invalid state impossible:

## Read-only Record Structs for each Field

- Each field in the domain model is represented by a `readonly record struct` that enforces the invariants of the data type it models.
- Each type has only a private constructor that cannot be used from outide the type to create an instanc.
- Each type defines a factory method that enforce the invariants before calling the private constructor.
- The raw value can be retrieved via a read-only property.

```csharp
// Must begin with an 'I' and be followed by 9 digits
public readonly record struct InvoiceNumber
{
    private static readonly Regex ValidationPattern = @"^I\d{9}$";

    // Read-Only accessor for the underlying value
    public string Value { get; }

    // private constructor is the only way to assign the value
    private InvoiceNumber(string value) 
    {
        this.Value = value;
    }

    // Factory method enforces invariants before calling constructor
    public static InvoiceNumber Create(string value)
    {
        // Validate the input data and throw if the data is not valid for the type
        if (!ValidationPattern.IsMatch(value))
        {
            throw new InvalidInvoiceNumberException(value);
        }

        // Only construct the type if the data was valid
        return new InvoiceNumber(value);
    }
}
```

## Read-only Records composed of validated field types for Complex Types

- Each complex type is defined as a read-only Record containing the relevant fields as get/init-only properties, where the fields are all themselves validated read-only record structs

- If any combination of valid fields is considered valid for the complex type, a public constructor can be used, since the field-level validations are sufficient.  However, if logic must be executed across multiple fields to validate the complex type, it should also use a private constructor with a factory method that enforces the validation.

```csharp

// Any combination of valid fields is considered valid
public record BillingDocument
{
    public required InvoiceNumber InvoiceNumber { get; init; }
    public required InvoiceDate InvoiceDate { get; init; }
    public required AccountNumber AccountNumber { get; init; }
}


// Requires evaluation of fields together to be considered valid
public record BillingDocumentSearch 
{
    public required InvoiceDate BeginDate { get; init; }
    public required InvoiceDate EndDate { get; init; }
    public required AccountNumber AccountNumber { get; init; }

    // Private constructor so the record cannot be constructed without valdation
    private BillingDocumentSearch(InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber) 
    {
        this.BeginDate = beginDate;
        this.EndDate = endDate;
        this.AccountNumber = accountNumber;
    }

    // Factory metod to compare begin date and end date and ensure the range is valid before constructing the record.
    public BillingDocumentSearch Create(InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber) 
    {
        if (endDate < beginDate)
        {
            throw new InvalidDateRangeException(beginDate, endDate);
        }

        return new BillingDocumentSearch(beginDate, endDate, accountNumber);
    }
}
```

## Note on Real-World Usage

In many cases, a complex type would not be built from a set of individual fields, but from a request object sent in with all the fields together.  Likewise, a complex type made up of fields that are themselves structs rather than primitives would not be very serialization-friendly.  As such, the factory method for complex types will likely use an interface composing the required fields and/or an unvalidated, serialization-friendly DTO type as the input parameter, and similar to the `.Value` property on the field-level structs, a `ToDto()`or similar method would exist on the type to allow automatic conversion to the serialization-friendly structure.