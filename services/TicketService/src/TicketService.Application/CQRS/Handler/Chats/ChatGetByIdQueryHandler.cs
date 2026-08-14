using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

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

        var isManagerOrAdmin = TicketQueryHelper.IsManagerOrAdmin(request.ActorRoles);

        if (chat is null || (chat.IsInternal && !canViewInternalChats))
            return Fail(404, "Comment not found.");

        var loadChildData = !chat.IsDeleted || isManagerOrAdmin;

        var attachments = loadChildData
            ? await _unitOfWork.TicketAttachments.GetAllAsync()
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
                    Url = a.Url,
                    IsInline = a.IsInline,
                    DownloadCount = a.DownloadCount,
                    VirusScanStatus = a.VirusScanStatus,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync(cancellationToken)
            : [];

        var chatIds = loadChildData ? new[] { chat.Id } : [];
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_unitOfWork, chatIds, cancellationToken);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_unitOfWork, chatIds, cancellationToken);
        var translationsByChat = await ChatChildDataLoader.LoadTranslationsForUserAsync(_unitOfWork, chatIds, request.ActorUserId, cancellationToken);

        var dto = new TicketChatDTO
        {
            Id = chat.Id.ToString(),
            TicketId = chat.TicketId.ToString(),
            AuthorUserId = chat.AuthorUserId.ToString(),
            AuthorRole = chat.AuthorRole,
            AuthorDisplayName = chat.AuthorDisplayName,
            IsInternal = chat.IsInternal,
            CreatedAt = chat.CreatedAt,
            ParentChatId = chat.ParentChatId?.ToString(),
            ThreadRootId = chat.ThreadRootId?.ToString(),
            ReplyCount = chat.ReplyCount,
            IsPinned = chat.IsPinned,
            PinnedAt = chat.PinnedAt,
            PinnedByUserId = chat.PinnedByUserId?.ToString(),
            IsDeleted = chat.IsDeleted,
            Body = chat.IsDeleted && !isManagerOrAdmin ? "This message has been deleted." : chat.Body,
            BodyHtml = chat.IsDeleted && !isManagerOrAdmin ? null : chat.BodyHtml,
            BodyFormat = chat.IsDeleted && !isManagerOrAdmin ? default : chat.BodyFormat,
            AttachmentFileIds = loadChildData ? attachments.Select(a => a.FileId).ToList() : [],
            EditedAt = chat.IsDeleted && !isManagerOrAdmin ? null : chat.EditedAt,
            EditCount = chat.IsDeleted && !isManagerOrAdmin ? 0 : chat.EditCount,
            LastEditedByUserId = chat.IsDeleted && !isManagerOrAdmin ? null : chat.LastEditedByUserId?.ToString(),
            Attachments = loadChildData ? attachments : [],
            Mentions = loadChildData ? (mentionsByChat.TryGetValue(chat.Id, out var m) ? m : new()) : [],
            Reactions = loadChildData ? (reactionsByChat.TryGetValue(chat.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()) : new(),
            ActiveTranslation = loadChildData ? (translationsByChat.TryGetValue(chat.Id, out var tr) ? tr : null) : null,
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
