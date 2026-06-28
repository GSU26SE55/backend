using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatTemplates;

public class ChatTemplateCreateCommand : IRequest<CommonResponse<ChatTemplateDTO>>, IValidatable<CommonResponse<ChatTemplateDTO>>
{
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
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung chi tiết của template, hỗ trợ place-holders.
    /// </summary>
    /// <example>Xin chào {{CustomerName}}, tôi là {{StaffName}} từ bộ phận hỗ trợ kỹ thuật.</example>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Phân loại của template.
    /// </summary>
    /// <example>Greeting</example>
    public ChatTemplateCategoryEnum Category { get; set; }

    /// <summary>
    /// Có phải là template nội bộ mặc định không.
    /// </summary>
    /// <example>true</example>
    public bool IsInternalDefault { get; set; }

    /// <summary>
    /// Phạm vi sử dụng của template (Personal, Team, Global).
    /// </summary>
    /// <example>Personal</example>
    public ChatTemplateScopeEnum Scope { get; set; } = ChatTemplateScopeEnum.Personal;

    public Task<CommonResponse<ChatTemplateDTO>> ValidateAsync()
    {
        var response = new CommonResponse<ChatTemplateDTO>();

        if (string.IsNullOrWhiteSpace(Name))
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Tên template không được để trống." });

        if (string.IsNullOrWhiteSpace(Content))
            response.ListErrors.Add(new Errors { Field = "Content", Detail = "Nội dung template không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
