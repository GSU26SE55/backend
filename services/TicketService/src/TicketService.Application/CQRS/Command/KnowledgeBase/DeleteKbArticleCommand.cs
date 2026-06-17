using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class DeleteKbArticleCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    public Guid ArticleId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (ArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ArticleId", Detail = "ID bài viết không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
