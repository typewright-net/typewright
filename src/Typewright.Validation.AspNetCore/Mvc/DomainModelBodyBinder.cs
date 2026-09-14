using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Typewright.Validation.AspNetCore.Mvc;

/// <summary>
/// Binds a domain model from the JSON request body by reading its DTO shape and
/// validating through <c>TryCreate</c>, reporting every failure through <c>ModelState</c>
/// under its JSON path.
/// </summary>
public sealed class DomainModelBodyBinder<TModel, TDto> : IModelBinder
    where TModel : class, IDomainModel<TModel, TDto>
    where TDto : class
{
    /// <inheritdoc />
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var request = bindingContext.HttpContext.Request;
        var modelState = bindingContext.ModelState;
        var prefix = bindingContext.ModelName;

        if (!request.HasJsonContentType())
        {
            modelState.TryAddModelError(
                ValidationErrors.CombinePath(prefix, "$"),
                "The request body must have a JSON content type.");
            return;
        }

        TDto? dto;
        try
        {
            dto = await request.ReadFromJsonAsync<TDto>(bindingContext.HttpContext.RequestAborted);
        }
        catch (JsonException exception)
        {
            modelState.TryAddModelError(
                ValidationErrors.CombinePath(prefix, "$"),
                $"The request body is not valid JSON: {exception.Message}");
            return;
        }

        if (dto is null)
        {
            modelState.TryAddModelError(
                ValidationErrors.CombinePath(prefix, "$"), "A request body is required.");
            return;
        }

        var validation = TModel.TryCreate(dto);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                modelState.TryAddModelError(
                    ValidationErrors.CombinePath(prefix, error.Path), error.Message);
            }

            return;
        }

        bindingContext.Result = ModelBindingResult.Success(validation.Value);
    }
}
