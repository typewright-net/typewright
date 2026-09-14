namespace Typewright.Validation;

/// <summary>
/// One validation failure: a typed <see cref="IDomainError"/> at a location in the JSON
/// convention of the input shape (for example <c>invoiceNumber</c> or
/// <c>lineItems[2].quantity</c>; an empty path means the value being validated as a
/// whole).
/// </summary>
/// <param name="Path">Where the failure occurred.</param>
/// <param name="Error">What went wrong, as structured data.</param>
public readonly record struct ValidationFailure(string Path, IDomainError Error);
