using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatTemplates;

public class ChatTemplateUpdateCommand : IRequest<CommonResponse<ChatTemplateDTO>>
{
    /// <summary>
    /// ID của mẫu phản hồi/chat.
    /// </summary>
    [JsonIgnore]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Tên của chat template.
    /// </summary>
    /// <example>Lời chào khách hàng</example>
    public string? Name { get; set; }

    /// <summary>
    /// Nội dung chi tiết của template, hỗ trợ place-holders.
    /// </summary>
    /// <example>Xin chào {{CustomerName}}, tôi là {{StaffName}} từ bộ phận hỗ trợ kỹ thuật.</example>
    public string? Content { get; set; }

    /// <summary>
    /// Phân loại của template.
    /// </summary>
    /// <example>Greeting</example>
    public ChatTemplateCategoryEnum? Category { get; set; }

    /// <summary>
    /// Có phải là template nội bộ mặc định không.
    /// </summary>
    /// <example>true</example>
    public bool? IsInternalDefault { get; set; }

    /// <summary>
    /// Trạng thái hoạt động của template.
    /// </summary>
    /// <example>true</example>
    public bool? IsActive { get; set; }
}
