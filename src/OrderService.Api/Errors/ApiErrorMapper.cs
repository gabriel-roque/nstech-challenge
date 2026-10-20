using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Common;

namespace OrderService.Api.Errors;

public static class ApiErrorMapper
{
    public static ObjectResult ToActionResult(ControllerBase controller, AppError error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Type.ToString(),
            Detail = error.Message,
            Instance = controller.HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;
        if (error.Field is not null)
        {
            problem.Extensions["field"] = error.Field;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static ObjectResult Validation(ControllerBase controller, string code, string message, string? field = null) =>
        ToActionResult(controller, new AppError(ErrorType.Validation, code, message, field));
}

