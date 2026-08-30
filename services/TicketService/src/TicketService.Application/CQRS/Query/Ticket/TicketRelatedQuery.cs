using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

/// <summary>
/// Các ticket còn mở LIÊN QUAN tới ticket này — cùng site, hoặc đã link cha–con.
///
/// Sinh ra từ tình huống thật: cabinet bốc khói thì hệ thống tạo 1 ticket environmental, đồng
/// thời Customer báo lỗi từng cục pin trong cabinet đó — 5 ticket, cùng một nguyên nhân gốc.
/// Trước đây không có cách nào nhìn thấy chúng cùng lúc, mà merge thì sai (merge đóng ticket
/// nguồn với CloseReason = MergedDuplicate và dừng SLA — trong khi các cục pin vẫn phải được
/// kiểm tra sau khi dập xong sự cố).
/// </summary>
public class TicketRelatedQuery : IRequest<CommonResponse<List<TicketDTO>>>
{
    /// <summary>Ticket đang mở (ticket "gốc" của câu hỏi).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }
    public IReadOnlyCollection<string> ActorRoles { get; set; } = Array.Empty<string>();
}
