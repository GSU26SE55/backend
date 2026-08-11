using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleVersionByIdQueryHandler : IRequestHandler<GetKbArticleVersionByIdQuery, CommonResponse<KbArticleVersionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbArticleVersionByIdQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleVersionDTO>> Handle(GetKbArticleVersionByIdQuery query, CancellationToken ct)
    {
        var version = await _uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.Id == query.VersionId, ct);

        if (version == null)
            return Fail(404, "Article version not found.");

        return new CommonResponse<KbArticleVersionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = KnowledgeBaseMapper.ToVersionDto(version)
        };
    }

    private static CommonResponse<KbArticleVersionDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleVersionDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
