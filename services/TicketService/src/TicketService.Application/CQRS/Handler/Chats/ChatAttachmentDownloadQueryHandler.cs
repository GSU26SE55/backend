using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentDownloadQueryHandler : IRequestHandler<ChatAttachmentDownloadQuery, CommonResponse<string>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ChatOptions _opts;

    public ChatAttachmentDownloadQueryHandler(ITicketUnitOfWork unitOfWork, IOptions<ChatOptions> opts)
    {
        _unitOfWork = unitOfWork;
        _opts = opts.Value;
    }

    public async Task<CommonResponse<string>> Handle(ChatAttachmentDownloadQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.ActorUserId, request.ActorRoles))
            return Fail(403, "Forbidden");

        var attachment = await _unitOfWork.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.FileId == request.AttachmentId
                     && a.ChatId == request.ChatId
                     && a.TicketId == request.TicketId
                     && !a.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (attachment is null)
            return Fail(404, "Attachment not found");

        var downloadUrl = $"{_opts.VirusScan.FileStorageBaseUrl.TrimEnd('/')}/api/files/{attachment.FileId}/download";

        if (!_opts.Features.EnableVirusScan)
            return new CommonResponse<string> { IsSuccess = true, StatusCode = 200, Data = downloadUrl };

        return attachment.VirusScanStatus switch
        {
            VirusScanStatusEnum.Infected => Fail(451, "File is infected and cannot be downloaded"),
            VirusScanStatusEnum.Clean => new CommonResponse<string> { IsSuccess = true, StatusCode = 200, Data = downloadUrl },
            _ => new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 202,
                Message = "File is pending virus scan. Please retry shortly."
            }
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
