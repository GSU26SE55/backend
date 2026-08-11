using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatConvertToKbDraftCommand : IRequest<CommonResponse<KbArticleActionDTO>>, IValidatable<CommonResponse<KbArticleActionDTO>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    /// <summary>
    /// ID của người dùng hiện tại thực hiện hành động.
    /// </summary>
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    /// <summary>
    /// Tiêu đề.
    /// </summary>
    public string? Title { get; set; }
    public TicketCategoryEnum? Category { get; set; }

    public Task<CommonResponse<KbArticleActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDTO>();

        if (Title != null && Title.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title must not exceed 200 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
