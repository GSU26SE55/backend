using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.ChatTemplates;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatTemplatesQueryHandler : IRequestHandler<ChatTemplatesQuery, CommonResponse<PaginationResponse<ChatTemplateDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatTemplatesQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<PaginationResponse<ChatTemplateDTO>>> Handle(ChatTemplatesQuery request, CancellationToken ct)
    {
        var isAdmin = request.ActorRoles.Contains("Admin") || request.ActorRoles.Contains("Manager");

        var query = _uow.ChatTemplates.GetAllAsync()
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        // Personal templates: chỉ thấy của mình; Global/Team: tất cả staff thấy
        if (!isAdmin)
        {
            query = query.Where(t =>
                t.Scope == ChatTemplateScopeEnum.Global
                || (t.Scope == ChatTemplateScopeEnum.Personal && t.CreatedByUserId == request.ActorUserId)
                || t.Scope == ChatTemplateScopeEnum.Team);
        }

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        if (request.Scope.HasValue)
            query = query.Where(t => t.Scope == request.Scope.Value);

        if (request.IsActive.HasValue)
            query = query.Where(t => t.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(t => t.Name.Contains(request.Search) || t.Content.Contains(request.Search));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.UsageCount)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return new CommonResponse<PaginationResponse<ChatTemplateDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<ChatTemplateDTO>
            {
                Items = items.Select(ChatTemplateCreateCommandHandler.ToDTO).ToList(),
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
