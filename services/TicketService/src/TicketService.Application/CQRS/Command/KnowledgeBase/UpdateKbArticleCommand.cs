using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class UpdateKbArticleCommand : IRequest<CommonResponse<KbArticleDTO>>, IValidatable<CommonResponse<KbArticleDTO>>
{
    /// <summary>
    /// Article id.
    /// </summary>
    [JsonIgnore]
    public Guid ArticleId { get; set; }
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }
    /// <summary>
    /// Vai trò của người dùng hiện tại.
    /// </summary>
    [JsonIgnore]
    public string CurrentUserRole { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    /// <summary>
    /// Tiêu đề.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string? ChangeDescription { get; set; }

    public Task<CommonResponse<KbArticleDTO>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            ListErrors = new List<Errors>()
        };

        if (ArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ArticleId", Detail = "ID bài viết không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được để trống." });
        else if (Title.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được vượt quá 200 ký tự." });

        if (string.IsNullOrWhiteSpace(Content))
            response.ListErrors.Add(new Errors { Field = "Content", Detail = "Nội dung không được để trống." });
        else if (Content.Length > 50000)
            response.ListErrors.Add(new Errors { Field = "Content", Detail = "Nội dung không được vượt quá 50000 ký tự." });

        if (!Enum.IsDefined(typeof(TicketCategoryEnum), Category))
            response.ListErrors.Add(new Errors { Field = "Category", Detail = "Danh mục không hợp lệ." });

        if (Tags != null && Tags.Count > 10)
            response.ListErrors.Add(new Errors { Field = "Tags", Detail = "Tối đa 10 thẻ." });

        if (Tags != null && Tags.Any(t => t.Length > 50))
            response.ListErrors.Add(new Errors { Field = "Tags", Detail = "Mỗi thẻ không được vượt quá 50 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
