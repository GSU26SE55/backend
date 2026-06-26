using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatTemplateCreate;

public class ChatTemplateCreateCommand : IRequest<CommonResponse<ChatTemplateDTO>>, IValidatable<CommonResponse<ChatTemplateDTO>>
{
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatTemplateCategoryEnum Category { get; set; }
    public bool IsInternalDefault { get; set; }
    public ChatTemplateScopeEnum Scope { get; set; } = ChatTemplateScopeEnum.Personal;
    public Guid? TeamId { get; set; }

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
