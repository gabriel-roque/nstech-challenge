using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.Api.Errors;
using OrderService.Application.Auth;
using OrderService.Application.Contracts;

namespace OrderService.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(AuthenticateHandler handler) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("token")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiErrorMapper.ToActionResult(this, result.Error!);
    }
}

