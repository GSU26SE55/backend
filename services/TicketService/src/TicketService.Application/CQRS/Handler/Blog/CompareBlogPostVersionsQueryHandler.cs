using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.Blog;

public class CompareBlogPostVersionsQueryHandler : IRequestHandler<CompareBlogPostVersionsQuery, CommonResponse<BlogDiffDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public CompareBlogPostVersionsQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogDiffDTO>> Handle(CompareBlogPostVersionsQuery request, CancellationToken ct)
    {
        if (request.OldVersionNumber <= 0 || request.NewVersionNumber <= 0 || request.OldVersionNumber == request.NewVersionNumber)
            return new CommonResponse<BlogDiffDTO> { IsSuccess = false, StatusCode = 400, Message = "Số version không hợp lệ." };

        var versions = await _uow.BlogPostVersions.GetAllAsync()
            .Where(x => x.BlogPostId == request.BlogPostId && !x.IsDeleted
                && (x.VersionNumber == request.OldVersionNumber || x.VersionNumber == request.NewVersionNumber))
            .ToListAsync(ct);

        var oldVersion = versions.FirstOrDefault(x => x.VersionNumber == request.OldVersionNumber);
        var newVersion = versions.FirstOrDefault(x => x.VersionNumber == request.NewVersionNumber);

        if (oldVersion == null)
            return new CommonResponse<BlogDiffDTO> { IsSuccess = false, StatusCode = 404, Message = $"Không tìm thấy version {request.OldVersionNumber}." };
        if (newVersion == null)
            return new CommonResponse<BlogDiffDTO> { IsSuccess = false, StatusCode = 404, Message = $"Không tìm thấy version {request.NewVersionNumber}." };

        return new CommonResponse<BlogDiffDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BlogDiffDTO
            {
                OldVersionNumber = oldVersion.VersionNumber,
                NewVersionNumber = newVersion.VersionNumber,
                OldContentHtml = KnowledgeBaseMapper.J(oldVersion.ContentHtml),
                NewContentHtml = KnowledgeBaseMapper.J(newVersion.ContentHtml),
            }
        };
    }

}
