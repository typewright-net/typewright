using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Typewright.Validation.AspNetCore.Mvc;

/// <summary>
/// MVC binding for domain types: values bind from route/query/header/form value providers,
/// models bind from the JSON body via their DTO. Both report failures through
/// <c>ModelState</c> using the same JSON paths as minimal API binding, so the
/// <c>[ApiController]</c> automatic 400 carries the same <c>errors</c> shape.
/// Register with <c>services.AddControllers(options =&gt; options.AddTypewright())</c>.
/// </summary>
public sealed class TypewrightModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modelType = context.Metadata.ModelType;
        var unwrapped = Nullable.GetUnderlyingType(modelType) ?? modelType;

        foreach (var contract in unwrapped.GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(IDomainValue<,>) && contract.GetGenericArguments() is [var value, var raw]
                && value == unwrapped
                && (raw == typeof(string) || raw.GetInterfaces().Any(candidate =>
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IParsable<>)
                    && candidate.GetGenericArguments()[0] == raw)))
            {
                return (IModelBinder)Activator.CreateInstance(
                    typeof(DomainValueModelBinder<,>).MakeGenericType(unwrapped, raw))!;
            }

            if (definition == typeof(IDomainModel<,>) && contract.GetGenericArguments() is [var model, var dto]
                && model == unwrapped
                && context.BindingInfo.BindingSource == BindingSource.Body)
            {
                return (IModelBinder)Activator.CreateInstance(
                    typeof(DomainModelBodyBinder<,>).MakeGenericType(unwrapped, dto))!;
            }
        }

        return null;
    }
}
