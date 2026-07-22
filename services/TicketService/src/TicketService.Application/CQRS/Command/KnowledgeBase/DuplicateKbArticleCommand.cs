using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

/// <summary>
/// Sao chép 1 bài KB có sẵn thành bài mới (title = "{title}_copy", status = Draft).
/// Trả về Id bài mới để FE điều hướng thẳng vào trang chỉnh sửa.
/// </summary>
public class DuplicateKbArticleCommand : IRequest<CommonResponse<KbArticleActionDTO>>
{
    /// <summary>Id bài gốc (lấy từ route).</summary>
    [BindNever]
    public Guid SourceId { get; set; }

    /// <summary>Id user hiện tại (controller gán).</summary>
    [BindNever]
    public Guid CurrentUserId { get; set; }
}
