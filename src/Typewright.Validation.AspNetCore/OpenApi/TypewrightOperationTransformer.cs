using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Typewright.Validation.AspNetCore.OpenApi;

/// <summary>
/// Completes operations with domain-typed parameters: parameters bound through
/// <c>BindAsync</c> get their raw-primitive schema (which the framework cannot infer), a
/// domain-model body gets its DTO schema, and every such operation gains the 400
/// validation-problem response produced by <c>UseTypewright()</c>.
/// </summary>
public sealed class TypewrightOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var handler = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<MethodInfo>()
                .FirstOrDefault()
            ?? (context.Description.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo;
        if (handler is null)
        {
            return;
        }

        var hasDomainParameters = false;
        foreach (var parameter in handler.GetParameters())
        {
            if (parameter.Name is null
                || DomainTypeRegistry.Find(parameter.ParameterType) is not { } info)
            {
                continue;
            }

            hasDomainParameters = true;
            if (info.IsModel)
            {
                await EnsureRequestBodyAsync(operation, context, info, cancellationToken);
            }
            else
            {
                EnsureParameter(operation, context, parameter, info);
            }
        }

        if (hasDomainParameters)
        {
            await EnsureValidationResponseAsync(operation, context, cancellationToken);
        }
    }

    private static void EnsureParameter(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        ParameterInfo parameter,
        DomainTypeInfo info)
    {
        var name = parameter.Name!;
        var relativePath = context.Description.RelativePath ?? "";
        var isRouteParameter = relativePath.Contains($"{{{name}}}", StringComparison.OrdinalIgnoreCase);

        operation.Parameters ??= [];
        var existing = operation.Parameters
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var created = new OpenApiParameter
            {
                Name = name,
                In = isRouteParameter ? ParameterLocation.Path : ParameterLocation.Query,
                Required = isRouteParameter || Nullable.GetUnderlyingType(parameter.ParameterType) is null,
            };
            operation.Parameters.Add(created);
            existing = created;
        }

        if (existing is OpenApiParameter concrete)
        {
            var schema = concrete.Schema as OpenApiSchema ?? new OpenApiSchema();
            OpenApiSchemaMapping.ApplyDomainValue(schema, info);
            concrete.Schema = schema;
        }
    }

    private static async Task EnsureRequestBodyAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        DomainTypeInfo info,
        CancellationToken cancellationToken)
    {
        var dtoSchema = await context.GetOrCreateSchemaAsync(info.RawType, cancellationToken: cancellationToken);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = dtoSchema },
            },
        };
    }

    private static async Task EnsureValidationResponseAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Responses ??= [];
        if (operation.Responses.ContainsKey("400"))
        {
            return;
        }

        var problemSchema = await context.GetOrCreateSchemaAsync(
            typeof(HttpValidationProblemDetails), cancellationToken: cancellationToken);
        operation.Responses["400"] = new OpenApiResponse
        {
            Description = "One or more validation errors occurred. Every failure is reported, "
                + "keyed by its JSON path; the errorCodes extension carries the stable codes.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = problemSchema },
            },
        };
    }
}
