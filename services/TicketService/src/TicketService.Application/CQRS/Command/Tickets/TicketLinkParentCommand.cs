using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

/// <summary>
/// Gắn (hoặc gỡ) ticket này vào một ticket cha cùng nguyên nhân gốc.
///
/// KHÁC merge một cách có chủ đích: link KHÔNG đóng ticket, KHÔNG dừng SLA, KHÔNG chuyển
/// attachment. Sự cố môi trường ở cabinet và các ticket pin trong cabinet đó cùng một nguyên
/// nhân, nhưng KHÔNG cùng khối lượng công việc — dập xong đám cháy thì từng cục pin vẫn phải
/// được kiểm tra, nên ticket con phải sống tiếp với SLA riêng của nó.
/// </summary>
public class TicketLinkParentCommand : IRequest<TicketActionResponse>
{
    /// <summary>Ticket con — ticket sẽ được gắn vào cha.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    /// <summary>Ticket cha. Null = gỡ liên kết hiện có.</summary>
    public Guid? ParentTicketId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid ActorId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string? ActorName { get; set; }
}
