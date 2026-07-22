using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentAddCommandHandler : IRequestHandler<ChatAttachmentAddCommand, CommonResponse<TicketAttachmentDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ChatOptions _chatOptions;

    public ChatAttachmentAddCommandHandler(ITicketUnitOfWork uow, IOptions<ChatOptions> chatOptions)
    {
        _uow = uow;
        _chatOptions = chatOptions.Value;
    }

    public async Task<CommonResponse<TicketAttachmentDTO>> Handle(ChatAttachmentAddCommand request, CancellationToken ct)
    {
        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (ticket.Status == TicketStatusEnum.Closed)
            return Fail(400, "Không thể thêm đính kèm khi ticket đã đóng.");

        var isAuthor = chat.AuthorUserId == request.UserId;
        var isManagerOrAdmin = request.UserRole == ActorRoleEnum.Manager || request.UserRole == ActorRoleEnum.Admin;
        if (!isAuthor && !isManagerOrAdmin)
            return Fail(403, "Không có quyền thêm đính kèm vào bình luận này.");

        var currentCount = await _uow.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .CountAsync(a => a.ChatId == chat.Id && !a.IsDeleted, ct);
        if (currentCount >= _chatOptions.MaxAttachmentsPerChat)
            return Fail(400, $"Đã đạt giới hạn tối đa {_chatOptions.MaxAttachmentsPerChat} đính kèm/bình luận.");

        if (request.SizeBytes > _chatOptions.MaxAttachmentSizeBytes)
            return Fail(400, $"Kích thước file vượt giới hạn {_chatOptions.MaxAttachmentSizeBytes / 1024 / 1024}MB.");

        if (!IsMimeTypeAllowed(request.ContentType, _chatOptions.AllowedAttachmentMimeTypes))
            return Fail(400, $"Loại file '{request.ContentType}' không được hỗ trợ.");

        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            ChatId = chat.Id,
            Chat = chat,
            UploadedByUserId = request.UserId,
            FileId = request.FileId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            Source = request.UserRole == ActorRoleEnum.Customer
                ? AttachmentSourceEnum.CustomerSubmission
                : AttachmentSourceEnum.StaffWork,
            Url = request.Url
        };

        await _uow.TicketAttachments.AddAsync(attachment);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<TicketAttachmentDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = MapToDto(attachment)
        };
    }

    private static bool IsMimeTypeAllowed(string contentType, List<string> allowedTypes)
    {
        foreach (var allowed in allowedTypes)
        {
            if (allowed.EndsWith("/*"))
            {
                var prefix = allowed[..^1]; // "image/*" -> "image/"
                if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(allowed, contentType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static TicketAttachmentDTO MapToDto(TicketAttachment a) => new()
    {
        Id = a.Id.ToString(),
        TicketId = a.TicketId.ToString(),
        ChatId = a.ChatId?.ToString(),
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
    };

    private static CommonResponse<TicketAttachmentDTO> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
