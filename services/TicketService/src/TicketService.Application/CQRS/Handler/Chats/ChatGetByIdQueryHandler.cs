using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatGetByIdQueryHandler : IRequestHandler<ChatGetByIdQuery, CommonResponse<TicketChatDTO>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatGetByIdQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<TicketChatDTO>> Handle(ChatGetByIdQuery request, CancellationToken cancellationToken)
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

        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);

        var chat = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.Id == request.ChatId && c.TicketId == request.TicketId)
            .FirstOrDefaultAsync(cancellationToken);

        var isManagerOrAdmin = TicketQueryHelper.IsManagerOrAdmin(request.ActorRoles);

        if (chat is null || (chat.IsDeleted && !isManagerOrAdmin) || (chat.IsInternal && !canViewInternalChats))
            return Fail(404, "Không tìm thấy bình luận.");

        var attachments = await _unitOfWork.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.ChatId == chat.Id && !a.IsDeleted)
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
                IsInline = a.IsInline,
                DownloadCount = a.DownloadCount,
                VirusScanStatus = a.VirusScanStatus,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var chatIds = new[] { chat.Id };
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_unitOfWork, chatIds, cancellationToken);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_unitOfWork, chatIds, cancellationToken);

        var dto = new TicketChatDTO
        {
            Id = chat.Id.ToString(),
            TicketId = chat.TicketId.ToString(),
            AuthorUserId = chat.AuthorUserId.ToString(),
            AuthorRole = chat.AuthorRole,
            AuthorDisplayName = chat.AuthorDisplayName,
            Body = chat.Body,
            IsInternal = chat.IsInternal,
            AttachmentFileIds = chat.AttachmentFileIds.Select(id => id.ToString()).ToList(),
            CreatedAt = chat.CreatedAt,
            EditedAt = chat.EditedAt,
            EditCount = chat.EditCount,
            LastEditedByUserId = chat.LastEditedByUserId?.ToString(),
            BodyFormat = chat.BodyFormat,
            BodyHtml = chat.BodyHtml,
            ParentChatId = chat.ParentChatId?.ToString(),
            ThreadRootId = chat.ThreadRootId?.ToString(),
            ReplyCount = chat.ReplyCount,
            IsPinned = chat.IsPinned,
            PinnedAt = chat.PinnedAt,
            PinnedByUserId = chat.PinnedByUserId?.ToString(),
            Attachments = attachments,
            Mentions = mentionsByChat.TryGetValue(chat.Id, out var m) ? m : new(),
            Reactions = reactionsByChat.TryGetValue(chat.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()
        };

        return new CommonResponse<TicketChatDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto
        };
    }

    private static CommonResponse<TicketChatDTO> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
