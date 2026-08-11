using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleByIdQueryHandler : IRequestHandler<GetKbArticleByIdQuery, CommonResponse<KbArticleDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCurrentUserService _currentUserService;

    public GetKbArticleByIdQueryHandler(ITicketUnitOfWork uow, ITicketCurrentUserService currentUserService)
    {
        _uow = uow;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<KbArticleDTO>> Handle(GetKbArticleByIdQuery query, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out _))
            return Fail(401, "Not logged in.");

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

        if (article == null || article.IsDeleted)
            return Fail(404, "Article not found.");

        // Block template access on Customer-facing endpoints
        if (query.RequireNonTemplate && article.IsTemplate)
            return Fail(404, "Article not found.");

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static CommonResponse<KbArticleDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
