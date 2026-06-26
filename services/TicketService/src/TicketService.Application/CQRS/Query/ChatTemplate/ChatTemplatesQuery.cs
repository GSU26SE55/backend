using System;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.ChatTemplate;

public class ChatTemplatesQuery : IRequest<CommonResponse<PaginationResponse<ChatTemplateDTO>>>
{
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    public ChatTemplateCategoryEnum? Category { get; set; }
    public ChatTemplateScopeEnum? Scope { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
