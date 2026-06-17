using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleVersionByIdQueryHandler : IRequestHandler<GetKbArticleVersionByIdQuery, CommonResponse<KbArticleVersionDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbArticleVersionByIdQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleVersionDto>> Handle(GetKbArticleVersionByIdQuery query, CancellationToken ct)
    {
        var version = await _uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.Id == query.VersionId, ct);

        if (version == null)
            return Fail(404, "Không tìm thấy phiên bản bài viết.");

        return new CommonResponse<KbArticleVersionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = KnowledgeBaseMapper.ToVersionDto(version)
        };
    }

    private static CommonResponse<KbArticleVersionDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleVersionDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
