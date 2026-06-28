using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAttachmentBatchAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentBatchAddCommandHandler : IRequestHandler<ChatAttachmentBatchAddCommand, CommonResponse<List<TicketAttachmentDTO>>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ChatOptions _chatOptions;

    public ChatAttachmentBatchAddCommandHandler(ITicketUnitOfWork uow, IOptions<ChatOptions> chatOptions)
    {
        _uow = uow;
        _chatOptions = chatOptions.Value;
    }

    public async Task<CommonResponse<List<TicketAttachmentDTO>>> Handle(ChatAttachmentBatchAddCommand request, CancellationToken ct)
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

        // Đếm số file hiện có trong chat
        var currentCount = await _uow.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .CountAsync(a => a.ChatId == chat.Id && !a.IsDeleted, ct);

        var slotsLeft = _chatOptions.MaxAttachmentsPerChat - currentCount;
        if (request.Files.Count > slotsLeft)
        {
            var msg = slotsLeft == 0
                ? $"Bình luận đã đạt giới hạn {_chatOptions.MaxAttachmentsPerChat} đính kèm. Vui lòng xóa bớt trước khi thêm."
                : $"Chỉ còn {slotsLeft} slot trống (tối đa {_chatOptions.MaxAttachmentsPerChat} đính kèm/bình luận). Bạn đang gửi {request.Files.Count} file — vui lòng bỏ bớt {request.Files.Count - slotsLeft} file.";
            return Fail(400, msg);
        }

        // Validate từng file trước khi lưu
        for (int i = 0; i < request.Files.Count; i++)
        {
            var f = request.Files[i];
            if (f.SizeBytes > _chatOptions.MaxAttachmentSizeBytes)
                return Fail(400, $"File [{f.FileName}] vượt giới hạn kích thước {_chatOptions.MaxAttachmentSizeBytes / 1024 / 1024}MB.");
            if (!IsMimeTypeAllowed(f.ContentType, _chatOptions.AllowedAttachmentMimeTypes))
                return Fail(400, $"File [{f.FileName}]: loại file '{f.ContentType}' không được hỗ trợ.");
        }

        var source = request.UserRole == ActorRoleEnum.Customer
            ? AttachmentSourceEnum.CustomerSubmission
            : AttachmentSourceEnum.StaffWork;

        var attachments = request.Files.Select(f => new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            ChatId = chat.Id,
            Chat = chat,
            UploadedByUserId = request.UserId,
            FileId = f.FileId,
            FileName = f.FileName,
            ContentType = f.ContentType,
            SizeBytes = f.SizeBytes,
            Source = source,
        }).ToList();

        await _uow.BeginTransactionAsync();
        try
        {
            foreach (var attachment in attachments)
                await _uow.TicketAttachments.AddAsync(attachment);
            await _uow.CommitTransactionAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackTransactionAsync();
            return Fail(500, "Lưu đính kèm thất bại, vui lòng thử lại.");
        }

        return new CommonResponse<List<TicketAttachmentDTO>>
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = attachments.Select(MapToDto).ToList()
        };
    }

    private static bool IsMimeTypeAllowed(string contentType, List<string> allowedTypes)
    {
        foreach (var allowed in allowedTypes)
        {
            if (allowed.EndsWith("/*"))
            {
                var prefix = allowed[..^1];
                if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(allowed, contentType, StringComparison.OrdinalIgnoreCase))
                return true;
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
        IsInline = a.IsInline,
        DownloadCount = a.DownloadCount,
        VirusScanStatus = a.VirusScanStatus,
        CreatedAt = a.CreatedAt
    };

    private static CommonResponse<List<TicketAttachmentDTO>> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
