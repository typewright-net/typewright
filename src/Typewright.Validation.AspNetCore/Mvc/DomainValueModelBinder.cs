using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Typewright.Validation.AspNetCore.Mvc;

/// <summary>
/// Binds a domain value from MVC value providers (route, query, header, form), reporting
/// failures through <c>ModelState</c> instead of throwing.
/// </summary>
public sealed class DomainValueModelBinder<TValue, TRaw> : IModelBinder
    where TValue : struct, IDomainValue<TValue, TRaw>
    where TRaw : IParsable<TRaw>
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        // For a missing top-level parameter MVC falls back to an empty ModelName;
        // FieldName still carries the parameter name for lookup and error paths.
        var name = string.IsNullOrEmpty(bindingContext.ModelName)
            ? bindingContext.FieldName
            : bindingContext.ModelName;
        var providerResult = bindingContext.ValueProvider.GetValue(name);

        if (providerResult == ValueProviderResult.None
            || string.IsNullOrEmpty(providerResult.FirstValue))
        {
            // A missing optional binds as null; a missing required value is an error.
            if (!bindingContext.ModelMetadata.IsReferenceOrNullableType)
            {
                bindingContext.ModelState.TryAddModelError(name, "A value is required.");
            }

            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(name, providerResult);
        var raw = providerResult.FirstValue;

        if (!TRaw.TryParse(raw, CultureInfo.InvariantCulture, out var rawValue))
        {
            bindingContext.ModelState.TryAddModelError(
                name, $"The value '{raw}' could not be parsed as {typeof(TRaw).Name}.");
            return Task.CompletedTask;
        }

        var validation = TValue.TryCreate(rawValue);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                bindingContext.ModelState.TryAddModelError(
                    ValidationErrors.CombinePath(name, error.Path), error.Message);
            }

            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(validation.Value);
        return Task.CompletedTask;
    }
}
