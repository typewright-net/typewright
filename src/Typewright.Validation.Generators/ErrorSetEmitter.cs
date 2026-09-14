using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Typewright.Validation.Generators;

/// <summary>Emits the error-set members shared by values and models.</summary>
internal static class ErrorSetEmitter
{
    /// <summary>The <c>ErrorTypes</c> initializer for a DomainTypeInfo object initializer.</summary>
    public static string ErrorTypesExpression(EquatableArray<ErrorTypeSpec> errorTypes)
    {
        if (errorTypes.Count == 0)
        {
            return "global::System.Array.Empty<global::System.Type>()";
        }

        var builder = new StringBuilder("new global::System.Type[] { ");
        for (var i = 0; i < errorTypes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("typeof(").Append(errorTypes[i].Display).Append(')');
        }

        return builder.Append(" }").ToString();
    }

    /// <summary>
    /// The generated exhaustive matcher: one <c>Func</c> per error type in the set, so
    /// growing the set breaks call sites at compile time until the new case is handled.
    /// </summary>
    public static string EmitMatchError(EquatableArray<ErrorTypeSpec> errorTypes)
    {
        if (errorTypes.Count == 0)
        {
            return "";
        }

        var names = HandlerNames(errorTypes);
        var parameters = new StringBuilder();
        var arms = new StringBuilder();
        for (var i = 0; i < errorTypes.Count; i++)
        {
            parameters.Append($",\n        global::System.Func<{errorTypes[i].Display}, TResult> {names[i]}");
            arms.Append($"            {errorTypes[i].Display} matched{i} => {names[i]}(matched{i}),\n");
        }

        return $$"""
    /// <summary>
    /// Exhaustively handles an error from this type's finite error set: one handler per
    /// possible error type. When the set grows, every call site fails to compile until
    /// the new case is handled.
    /// </summary>
    public static TResult MatchError<TResult>(
        global::Typewright.Validation.IDomainError error{{parameters}})
        => error switch
        {
{{arms}}            _ => throw new global::System.ArgumentOutOfRangeException(nameof(error), error?.GetType().FullName, "The error is not part of this domain type's error set."),
        };


""";
    }

    private static List<string> HandlerNames(EquatableArray<ErrorTypeSpec> errorTypes)
    {
        var names = new List<string>(errorTypes.Count);
        var used = new HashSet<string>();
        foreach (var errorType in errorTypes)
        {
            var simple = errorType.SimpleName;
            const string suffix = "Error";
            if (simple.Length > suffix.Length && simple.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                simple = simple.Substring(0, simple.Length - suffix.Length);
            }

            var name = char.ToLowerInvariant(simple[0]) + simple.Substring(1);
            if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                name = "@" + name;
            }

            var unique = name;
            var counter = 2;
            while (!used.Add(unique))
            {
                unique = name + counter++;
            }

            names.Add(unique);
        }

        return names;
    }
}
