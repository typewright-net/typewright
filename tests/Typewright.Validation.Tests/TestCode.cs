using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Typewright.Validation.Tests;

/// <summary>
/// A hand-implemented domain value (three ASCII uppercase letters) proving the contracts
/// are usable without the generator.
/// </summary>
[JsonConverter(typeof(DomainValueJsonConverter<TestCode, string>))]
public readonly record struct TestCode : IDomainValue<TestCode, string>
{
    private readonly string _value;

    private TestCode(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "This TestCode was not created through Create or TryCreate (default instance).");

    public static ValidationResult<TestCode> TryCreate(string? value)
    {
        if (value is null)
        {
            return ValidationResult<TestCode>.Failure(string.Empty, new RequiredError());
        }

        if (value.Length != 3 || !value.All(char.IsAsciiLetterUpper))
        {
            return ValidationResult<TestCode>.Failure(string.Empty, new PatternError("^[A-Z]{3}$"));
        }

        return ValidationResult<TestCode>.Success(new TestCode(value));
    }

    public static TestCode Create(string? value) => TryCreate(value).OrThrow();
}
