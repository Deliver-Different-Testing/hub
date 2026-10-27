using Hub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Hub.Controllers.Api;

/// <summary>
/// Base for the JSON endpoints the React SPA calls. Mutations require the antiforgery header,
/// and a request that fails model binding answers <c>{ "error": string }</c> rather than ProblemDetails.
/// </summary>
[ApiController]
[AutoValidateAntiforgeryToken]
public abstract class SpaApiControllerBase : ControllerBase
{
    public static void ConfigureInvalidModelState(ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            if (context.ActionDescriptor is ControllerActionDescriptor action
                && typeof(SpaApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
            {
                var message = context.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                return new BadRequestObjectResult(new ApiError(message ?? "Please check your input and try again."));
            }

            var problem = context.HttpContext.RequestServices
                .GetRequiredService<ProblemDetailsFactory>()
                .CreateValidationProblemDetails(context.HttpContext, context.ModelState);
            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json", "application/problem+xml" }
            };
        };
    }
}
