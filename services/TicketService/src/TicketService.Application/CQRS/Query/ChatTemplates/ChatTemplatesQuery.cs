using System;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.ChatTemplates;

public class ChatTemplatesQuery : IRequest<CommonResponse<PaginationResponse<ChatTemplateDTO>>>
{
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public ChatTemplateCategoryEnum? Category { get; set; }
    public ChatTemplateScopeEnum? Scope { get; set; }
    public bool? IsActive { get; set; }
    /// <summary>
    /// Search.
    /// </summary>
    public string? Search { get; set; }

    public int PageNumber { get; set; } = 1;
    /// <summary>
    /// Kích thước trang (số lượng bản ghi trên một trang).
    /// </summary>
    public int PageSize { get; set; } = 20;
}
