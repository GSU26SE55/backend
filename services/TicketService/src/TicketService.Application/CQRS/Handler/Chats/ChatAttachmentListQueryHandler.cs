using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentListQueryHandler : IRequestHandler<ChatAttachmentListQuery, CommonResponse<List<TicketAttachmentDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatAttachmentListQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<TicketAttachmentDTO>>> Handle(ChatAttachmentListQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() ?? t.PrimaryHandlerStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles))
            return Fail(403, "Forbidden");

        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);

        var chat = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.Id == request.ChatId && c.TicketId == request.TicketId)
            .FirstOrDefaultAsync(cancellationToken);

        if (chat is null || (chat.IsInternal && !canViewInternalChats))
            return Fail(404, "Không tìm thấy bình luận.");

        var attachments = await _unitOfWork.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.ChatId == chat.Id && !a.IsDeleted)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new TicketAttachmentDTO
            {
                Id = a.Id.ToString(),
                TicketId = a.TicketId.ToString(),
                ChatId = a.ChatId.ToString(),
                UploadedByUserId = a.UploadedByUserId.ToString(),
                FileId = a.FileId.ToString(),
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Source = a.Source,
                ThumbnailUrl = a.ThumbnailUrl,
                Url = a.Url,
                IsInline = a.IsInline,
                DownloadCount = a.DownloadCount,
                VirusScanStatus = a.VirusScanStatus,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<TicketAttachmentDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = attachments
        };
    }

    private static CommonResponse<List<TicketAttachmentDTO>> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
