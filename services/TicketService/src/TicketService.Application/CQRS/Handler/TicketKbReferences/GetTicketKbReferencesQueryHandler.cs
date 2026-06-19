using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.TicketKbReferences;
using TicketService.Application.DTOs.Response.TicketKbReferences;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.TicketKbReferences;

public class GetTicketKbReferencesQueryHandler : IRequestHandler<GetTicketKbReferencesQuery, CommonResponse<List<TicketKbReferenceDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetTicketKbReferencesQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<List<TicketKbReferenceDTO>>> Handle(GetTicketKbReferencesQuery query, CancellationToken ct)
    {
        var references = await _uow.TicketKbReferences.GetAllAsync()
            .Where(r => r.TicketId == query.TicketId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        // Lấy tiêu đề bài viết KB tương ứng (entity reference chỉ lưu Code, không lưu Title)
        var articleIds = references.Select(r => r.KbArticleId).Distinct().ToList();
        var titleById = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => articleIds.Contains(a.Id) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.Id, a => a.Title, ct);

        var data = references.Select(r => new TicketKbReferenceDTO
        {
            Id = r.Id.ToString(),
            TicketId = r.TicketId.ToString(),
            KbArticleId = r.KbArticleId.ToString(),
            KbArticleCode = r.KbArticleCode,
            KbArticleTitle = titleById.TryGetValue(r.KbArticleId, out var title) ? title : null,
            ReferencedByUserId = r.ReferencedByUserId.ToString(),
            ReferenceType = r.ReferenceType,
            Note = r.Note,
            CreatedAt = r.CreatedAt
        }).ToList();

        return new CommonResponse<List<TicketKbReferenceDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = data
        };
    }
}
