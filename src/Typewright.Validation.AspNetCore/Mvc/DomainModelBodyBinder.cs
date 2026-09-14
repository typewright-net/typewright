using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

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
        var prefix = bindingContext.ModelName;
        var renderer = bindingContext.HttpContext.RequestServices.GetService<IDomainErrorRenderer>()
            ?? DefaultDomainErrorRenderer.Instance;

        if (!request.HasJsonContentType())
        {
            bindingContext.ModelState.TryAddModelError(
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
            AddError(new ValidationFailure(
                ValidationErrors.CombinePath(prefix, "$"), new JsonInvalidError(exception.Message)));
            return;
        }

        if (dto is null)
        {
            AddError(new ValidationFailure(
                ValidationErrors.CombinePath(prefix, "$"), new RequiredError()));
            return;
        }

        var validation = TModel.TryCreate(dto);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                AddError(failure with { Path = ValidationErrors.CombinePath(prefix, failure.Path) });
            }

            return;
        }

        bindingContext.Result = ModelBindingResult.Success(validation.Value);

        void AddError(ValidationFailure failure)
            => bindingContext.ModelState.TryAddModelError(failure.Path, renderer.Render(failure));
    }
}
