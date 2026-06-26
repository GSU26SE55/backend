using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatConvertToKbDraft;

public class ChatConvertToKbDraftCommand : IRequest<CommonResponse<KbArticleActionDTO>>, IValidatable<CommonResponse<KbArticleActionDTO>>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public string? Title { get; set; }
    public TicketCategoryEnum? Category { get; set; }

    public Task<CommonResponse<KbArticleActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDTO>();

        if (Title != null && Title.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được vượt quá 200 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
