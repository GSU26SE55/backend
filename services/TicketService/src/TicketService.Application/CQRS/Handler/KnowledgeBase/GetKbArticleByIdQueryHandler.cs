using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleByIdQueryHandler : IRequestHandler<GetKbArticleByIdQuery, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCurrentUserService _currentUserService;

    public GetKbArticleByIdQueryHandler(ITicketUnitOfWork uow, ITicketCurrentUserService currentUserService)
    {
        _uow = uow;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(GetKbArticleByIdQuery query, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
            return Fail(401, "Chưa đăng nhập.");

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

        if (article == null || article.IsDeleted)
            return Fail(404, "Không tìm thấy bài viết.");

        // Role-based access control
        if (_currentUserService.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            if (article.IsInternalOnly || article.Status != KbArticleStatusEnum.Published)
            {
                return Fail(404, "Không tìm thấy bài viết.");
            }
        }

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static CommonResponse<KbArticleDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
