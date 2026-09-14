namespace Typewright.Validation;

/// <summary>
/// Produces presentation text for domain errors at the boundary. The domain reports
/// structured <see cref="IDomainError"/> data only; register a renderer in DI to control
/// (or localize) how failures read in responses. Paths and codes are stable regardless.
/// </summary>
public interface IDomainErrorRenderer
{
    /// <summary>Returns the message to present for <paramref name="failure"/>.</summary>
    string Render(ValidationFailure failure);
}

/// <summary>
/// The default renderer: each error's invariant <see cref="IDomainError.DefaultMessage"/>.
/// </summary>
public sealed class DefaultDomainErrorRenderer : IDomainErrorRenderer
{
    /// <summary>The shared instance.</summary>
    public static DefaultDomainErrorRenderer Instance { get; } = new();

    /// <inheritdoc />
    public string Render(ValidationFailure failure) => failure.Error.DefaultMessage;
}
