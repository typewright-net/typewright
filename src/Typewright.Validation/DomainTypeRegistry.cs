using System.Collections.Concurrent;
using System.Reflection;

namespace Typewright.Validation;

/// <summary>
/// Resolves the generated <see cref="DomainTypeInfo"/> descriptor for a domain type. The
/// lookup reflects once per type and caches, so consumers (OpenAPI transformers, schema
/// validation) stay reflection-free on the hot path.
/// </summary>
public static class DomainTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, DomainTypeInfo?> Cache = new();

    /// <summary>
    /// Returns the descriptor for <paramref name="type"/> (unwrapping <see cref="Nullable{T}"/>),
    /// or null when the type is not a described domain type.
    /// </summary>
    public static DomainTypeInfo? Find(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Cache.GetOrAdd(Nullable.GetUnderlyingType(type) ?? type, static resolved =>
            resolved.GetProperty("DomainTypeInfo", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as DomainTypeInfo);
    }

    /// <summary>
    /// When <paramref name="type"/> is the DTO of a domain model (the model's nested
    /// <c>Dto</c> type), returns the model's descriptor; otherwise null.
    /// </summary>
    public static DomainTypeInfo? FindModelByDto(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsNested && type.DeclaringType is { } model
            && Find(model) is { } info && info.RawType == type
            ? info
            : null;
    }
}
