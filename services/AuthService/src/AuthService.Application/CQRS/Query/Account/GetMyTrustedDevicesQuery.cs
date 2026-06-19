using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.TrustedDevice;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Query.Account;

/// <summary>
/// #AUTH-48: Liệt kê tất cả trusted device active của account hiện tại.
/// </summary>
public class GetMyTrustedDevicesQuery : IRequest<CommonResponse<List<TrustedDeviceDto>>>
{
    /// <summary>
    /// Account hiện tại — resolved từ JWT trong controller, KHÔNG bind từ query string/body
    /// để chống attacker spoof. [BindNever] chặn model binding, [JsonIgnore] ẩn khỏi Swagger.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }
}
