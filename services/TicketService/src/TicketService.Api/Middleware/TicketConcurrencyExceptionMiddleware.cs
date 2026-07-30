using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Middleware;

namespace TicketService.Api.Middleware;

/// <summary>
/// TicketService-only translation for stale server-side writes.
/// </summary>
public sealed class TicketConcurrencyExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public TicketConcurrencyExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            await CommonResponseWriter.WriteAsync(
                context.Response,
                StatusCodes.Status409Conflict,
                "Dữ liệu đã được thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.",
                data: new { errorCode = "CONCURRENCY_CONFLICT" });
        }
        catch (DbUpdateException exception) when (IsAllowedUniqueConstraint(exception))
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            await CommonResponseWriter.WriteAsync(
                context.Response,
                StatusCodes.Status409Conflict,
                "Dữ liệu đã được thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.",
                data: new { errorCode = "CONCURRENCY_CONFLICT" });
        }
    }

    private static bool IsAllowedUniqueConstraint(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: "23505",
                    ConstraintName: "ux_ticket_assignments_active_primary"
                        or "ux_ticket_participants_active_user"
                })
            {
                return true;
            }
        }

        return false;
    }
}
