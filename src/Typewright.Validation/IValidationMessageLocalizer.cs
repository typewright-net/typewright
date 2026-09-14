namespace Typewright.Validation;

/// <summary>
/// Localizes validation messages at the presentation boundary. Domain types carry
/// invariant messages; register an implementation in DI and the ASP.NET problem-details
/// writer maps every error through it (<see cref="ValidationError.Code"/> and
/// <see cref="ValidationError.Path"/> identify the failure; return
/// <see cref="ValidationError.Message"/> to keep the invariant text).
/// </summary>
public interface IValidationMessageLocalizer
{
    /// <summary>Returns the message to present for <paramref name="error"/>.</summary>
    string Localize(ValidationError error);
}
