using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.Api.Auth;
using OrderService.Api.Binding;
using OrderService.Api.Errors;
using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Application.Abstractions;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;

namespace OrderService.Api.Controllers;

[ApiController]
[Route("orders")]
public sealed class OrdersController(
    CreateOrderHandler createHandler,
    ConfirmOrderHandler confirmHandler,
    CancelOrderHandler cancelHandler,
    IOrderQueryService queries) : ControllerBase
{
    [Authorize]
    [HttpPost]
    [ProducesResponseType<OrderDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetCustomerId(out var authenticatedCustomerId))
        {
            return Unauthorized();
        }

        if (request.CustomerId != authenticatedCustomerId)
        {
            return ApiErrorMapper.ToActionResult(
                this,
                new AppError(ErrorType.Forbidden, "customer_mismatch", "A user may only create orders for itself.", "customerId"));
        }

        if (!Guid.TryParse(idempotencyKeyHeader, out var idempotencyKey) ||
            idempotencyKey == Guid.Empty)
        {
            return ApiErrorMapper.Validation(
                this,
                "idempotency_key_invalid",
                "Idempotency-Key must contain one non-empty UUID.",
                "Idempotency-Key");
        }

        var result = await createHandler.HandleAsync(request, idempotencyKey, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiErrorMapper.ToActionResult(this, result.Error!);
        }

        var outcome = result.Value!;
        var location = Url.ActionLink(nameof(Get), values: new { id = outcome.Order.Id });
        if (outcome.Created)
        {
            return Created(location, outcome.Order);
        }

        Response.Headers.Location = location;
        Response.Headers["Idempotency-Replayed"] = "true";
        return Ok(outcome.Order);
    }

    [Authorize]
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<OrderDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetCustomerId(out var customerId))
        {
            return Unauthorized();
        }

        var result = await confirmHandler.HandleAsync(id, customerId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiErrorMapper.ToActionResult(this, result.Error!);
    }

    [Authorize]
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetCustomerId(out var customerId))
        {
            return Unauthorized();
        }

        var result = await cancelHandler.HandleAsync(id, customerId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiErrorMapper.ToActionResult(this, result.Error!);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetCustomerId(out var customerId))
        {
            return Unauthorized();
        }

        var order = await queries.GetAsync(id, customerId, cancellationToken);
        return order is null
            ? ApiErrorMapper.ToActionResult(this, new AppError(ErrorType.NotFound, "order_not_found", "Order was not found."))
            : Ok(order);
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? customerId,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(customerId, out var parsedCustomerId) || parsedCustomerId == Guid.Empty)
        {
            return ApiErrorMapper.Validation(this, "customer_id_invalid", "customerId must be a non-empty UUID.", "customerId");
        }

        OrderStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var value) || !Enum.IsDefined(value))
            {
                return ApiErrorMapper.Validation(this, "status_invalid", "status is not a valid order status.", "status");
            }

            parsedStatus = value;
        }

        if (!Iso8601OffsetParser.TryParseOptional(from, out var parsedFrom) ||
            !Iso8601OffsetParser.TryParseOptional(to, out var parsedTo))
        {
            return ApiErrorMapper.Validation(this, "date_invalid", "from and to must use ISO 8601 with an offset or UTC.");
        }

        if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom.Value >= parsedTo.Value)
        {
            return ApiErrorMapper.Validation(this, "date_range_invalid", "from must be earlier than to.");
        }

        if (page < 1)
        {
            return ApiErrorMapper.Validation(this, "page_invalid", "page must be at least 1.", "page");
        }

        if (pageSize is < 1 or > 100)
        {
            return ApiErrorMapper.Validation(this, "page_size_invalid", "pageSize must be between 1 and 100.", "pageSize");
        }

        var result = await queries.ListAsync(
            new OrderListFilter(parsedCustomerId, parsedStatus, parsedFrom, parsedTo, page, pageSize),
            cancellationToken);
        return Ok(result);
    }
}
