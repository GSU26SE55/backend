using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatTemplateCreateCommandHandler : IRequestHandler<ChatTemplateCreateCommand, CommonResponse<ChatTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatTemplateCreateCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<ChatTemplateDTO>> Handle(ChatTemplateCreateCommand request, CancellationToken ct)
    {
        var template = new ChatTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Content = request.Content,
            Category = request.Category,
            IsInternalDefault = request.IsInternalDefault,
            Scope = request.Scope,
            CreatedByUserId = request.ActorUserId,
            IsActive = true,
            UsageCount = 0
        };

        await _uow.ChatTemplates.AddAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<ChatTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = ToDTO(template)
        };
    }

    internal static ChatTemplateDTO ToDTO(ChatTemplate t) => new()
    {
        Id = t.Id.ToString(),
        Name = t.Name,
        Content = t.Content,
        Category = t.Category,
        IsInternalDefault = t.IsInternalDefault,
        CreatedByUserId = t.CreatedByUserId.ToString(),
        Scope = t.Scope,
        UsageCount = t.UsageCount,
        IsActive = t.IsActive,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
