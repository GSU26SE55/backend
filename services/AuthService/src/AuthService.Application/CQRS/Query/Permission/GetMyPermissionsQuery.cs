using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Application.CQRS.Query.Permission;

/// <summary>
/// Lấy toàn bộ permission của role mà account hiện tại đang được gán.
/// </summary>
/// <remarks>
/// <c>AccountId</c> KHÔNG đến từ client — controller tự đọc claim <c>NameIdentifier</c> trong JWT
/// rồi gán vào property này. Hai attribute dưới bảo vệ khỏi mọi cố ý truyền AccountId từ
/// query string / body / form để query account của người khác.
/// </remarks>
public class GetMyPermissionsQuery : IRequest<MyPermissionsResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }
}
