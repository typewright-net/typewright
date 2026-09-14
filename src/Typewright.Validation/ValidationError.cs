namespace Typewright.Validation;

/// <summary>
/// A single validation failure.
/// </summary>
/// <param name="Path">
/// The location of the failure in the JSON convention of the input shape
/// (for example <c>invoiceNumber</c> or <c>lineItems[2].quantity</c>).
/// An empty string means the failure applies to the value being validated as a whole.
/// </param>
/// <param name="Code">A stable, machine-readable error code such as <c>required</c> or <c>pattern</c>.</param>
/// <param name="Message">A human-readable description of the failure.</param>
public readonly record struct ValidationError(string Path, string Code, string Message);
