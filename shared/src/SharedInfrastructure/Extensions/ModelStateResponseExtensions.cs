using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.Extensions;

/// <summary>
/// Override <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c> để [ApiController] không
/// trả <c>ValidationProblemDetails</c> mặc định (RFC 7807) — thay bằng <see cref="CommonResponse{T}"/>
/// với <c>ListErrors</c> chứa field + detail. Đảm bảo toàn hệ thống có 1 shape error duy nhất.
/// </summary>
public static class ModelStateResponseExtensions
{
    public static IServiceCollection AddCommonModelStateResponse(this IServiceCollection services)
    {
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var listErrors = new List<Errors>();
                foreach (var (key, entry) in context.ModelState)
                {
                    if (entry.Errors.Count == 0)
                        continue;

                    var detail = string.Join("; ", entry.Errors
                        .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                            ? (e.Exception?.Message ?? "Invalid value.")
                            : e.ErrorMessage));

                    listErrors.Add(new Errors
                    {
                        Field = string.IsNullOrEmpty(key) ? "request" : key,
                        Detail = detail
                    });
                }

                var payload = new CommonResponse<object>
                {
                    IsSuccess = false,
                    StatusCode = StatusCodes.Status400BadRequest,
                    Message = string.Empty,
                    Data = null,
                    ListErrors = listErrors
                };

                return new ObjectResult(payload)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/json" }
                };
            };
        });

        return services;
    }
}
