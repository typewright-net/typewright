using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Primitives;

namespace Typewright.Validation.AspNetCore;

/// <summary>
/// Binding helpers that domain types delegate to from their <c>BindAsync</c> methods.
/// A failed bind throws <see cref="DomainValidationException"/> carrying every error,
/// scoped to the parameter name (or <c>$</c> for whole-body failures); the exception is
/// turned into an RFC 9457 response by <c>UseTypewright()</c>.
/// </summary>
public static class TypewrightBinding
{
    /// <summary>
    /// Binds a domain value from a route value, query string entry, or header. An explicit
    /// <c>[FromRoute]</c>/<c>[FromQuery]</c>/<c>[FromHeader]</c> attribute wins; otherwise a
    /// route value with the parameter's name is used, then a query entry with that name.
    /// A missing value binds as null when the parameter is <c>Nullable&lt;TValue&gt;</c> and
    /// fails with a <c>required</c> error otherwise.
    /// </summary>
    public static ValueTask<TValue?> BindDomainValueAsync<TValue, TRaw>(HttpContext context, ParameterInfo parameter)
        where TValue : struct, IDomainValue<TValue, TRaw>
        where TRaw : IParsable<TRaw>
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameter);

        var (raw, name) = ResolveRawString(context, parameter);
        if (raw is null)
        {
            if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
            {
                return ValueTask.FromResult<TValue?>(null);
            }

            throw new DomainValidationException(name, new RequiredError());
        }

        if (!TRaw.TryParse(raw, CultureInfo.InvariantCulture, out var rawValue))
        {
            throw new DomainValidationException(name, new FormatError(raw, typeof(TRaw).Name));
        }

        var result = TValue.TryCreate(rawValue);
        if (!result.IsValid)
        {
            var errors = new ValidationErrors();
            errors.Merge(name, result.Errors);
            throw new DomainValidationException(errors);
        }

        return ValueTask.FromResult<TValue?>(result.Value);
    }

    /// <summary>
    /// Binds a domain model from the JSON request body by reading the model's DTO shape
    /// with the application's <see cref="JsonSerializerOptions"/> and validating through
    /// <c>TryCreate</c>. Malformed JSON fails with a single <c>json.invalid</c> error at
    /// path <c>$</c>; a missing body fails with <c>required</c> at <c>$</c>.
    /// </summary>
    public static async ValueTask<TModel?> BindDomainModelAsync<TModel, TDto>(HttpContext context, ParameterInfo parameter)
        where TModel : class, IDomainModel<TModel, TDto>
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameter);

        if (!context.Request.HasJsonContentType())
        {
            throw new BadHttpRequestException(
                "The request body must have a JSON content type.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        TDto? dto;
        try
        {
            dto = await context.Request.ReadFromJsonAsync<TDto>(context.RequestAborted);
        }
        catch (JsonException exception)
        {
            throw new DomainValidationException("$", new JsonInvalidError(exception.Message));
        }

        if (dto is null)
        {
            throw new DomainValidationException("$", new RequiredError());
        }

        return TModel.TryCreate(dto) switch
        {
            { IsValid: true } result => result.Value,
            var result => throw new DomainValidationException(result.Errors),
        };
    }

    private static (string? Raw, string Name) ResolveRawString(HttpContext context, ParameterInfo parameter)
    {
        var name = parameter.Name
            ?? throw new InvalidOperationException("Cannot bind a parameter that has no name.");

        foreach (var attribute in parameter.GetCustomAttributes(inherit: false))
        {
            switch (attribute)
            {
                case IFromRouteMetadata route:
                {
                    var routeName = route.Name ?? name;
                    return (RouteValueToString(context.Request.RouteValues[routeName]), routeName);
                }
                case IFromQueryMetadata query:
                {
                    var queryName = query.Name ?? name;
                    return (LastOrNull(context.Request.Query[queryName]), queryName);
                }
                case IFromHeaderMetadata header:
                {
                    var headerName = header.Name ?? name;
                    return (LastOrNull(context.Request.Headers[headerName]), headerName);
                }
            }
        }

        if (context.Request.RouteValues.TryGetValue(name, out var routeValue))
        {
            return (RouteValueToString(routeValue), name);
        }

        return (LastOrNull(context.Request.Query[name]), name);
    }

    private static string? RouteValueToString(object? routeValue)
        => routeValue as string ?? routeValue?.ToString();

    private static string? LastOrNull(StringValues values)
        => values.Count > 0 ? values[values.Count - 1] : null;
}
