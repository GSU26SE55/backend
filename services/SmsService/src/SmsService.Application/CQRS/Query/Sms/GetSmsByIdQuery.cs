using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SmsService.Domain.Entities;

namespace SmsService.Application.CQRS.Query.Sms;

/// <summary>
/// Đọc 1 SMS theo Id — dùng cho admin debug hoặc check status.
/// <para>
/// <c>SmsId</c> chỉ được set bởi controller từ route param <c>{id:guid}</c>:
/// <list type="bullet">
///   <item><c>[JsonIgnore]</c>: JSON body KHÔNG được set (đề phòng controller đổi sang <c>[FromBody]</c>).</item>
///   <item><c>[BindNever]</c>: ASP.NET model binder KHÔNG được lấy từ query/header/form (chỉ route).</item>
/// </list>
/// </para>
/// </summary>
public class GetSmsByIdQuery : IRequest<CommonResponse<SmsMessage>>
{
    [JsonIgnore]
    [BindNever]
    public Guid SmsId { get; set; }
}
