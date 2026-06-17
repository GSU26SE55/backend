using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class SuggestKbArticlesQueryHandler : IRequestHandler<SuggestKbArticlesQuery, CommonResponse<List<KbArticleSuggestDto>>>
{
    private readonly ITicketUnitOfWork _uow;

    public SuggestKbArticlesQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<List<KbArticleSuggestDto>>> Handle(SuggestKbArticlesQuery query, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == query.TicketId, ct);

        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var suggestions = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => !a.IsDeleted && a.Status == KbArticleStatusEnum.Published && !a.IsInternalOnly && a.Category == ticket.Category)
            .OrderByDescending(a => a.HelpfulCount)
            .ThenByDescending(a => a.ViewCount)
            .Take(5)
            .ToListAsync(ct);

        return new CommonResponse<List<KbArticleSuggestDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = suggestions.Select(KnowledgeBaseMapper.ToSuggestDto).ToList()
        };
    }

    private static CommonResponse<List<KbArticleSuggestDto>> Fail(int statusCode, string message)
    {
        return new CommonResponse<List<KbArticleSuggestDto>>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
