using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedKernels.Security;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentDownloadQueryHandler : IRequestHandler<ChatAttachmentDownloadQuery, CommonResponse<string>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ChatOptions _opts;
    private readonly IConfiguration _configuration;

    public ChatAttachmentDownloadQueryHandler(
        ITicketUnitOfWork unitOfWork,
        IOptions<ChatOptions> opts,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _opts = opts.Value;
        _configuration = configuration;
    }

    public async Task<CommonResponse<string>> Handle(ChatAttachmentDownloadQuery request, CancellationToken cancellationToken)
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

        var attachment = await _unitOfWork.TicketAttachments.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.FileId == request.AttachmentId
                     && a.ChatId == request.ChatId
                     && a.TicketId == request.TicketId
                     && !a.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (attachment is null)
            return Fail(404, "Attachment not found");

        // GH-723 — tới đây quyền đã được xác nhận (CanAccessTicket ở trên). Ký quyết định đó
        // thành grant ngắn hạn gắn với (fileId, người gọi) để FileStorageService xác minh
        // được, thay vì trả một URL trần mà ai đăng nhập cũng dùng được.
        var downloadUrl = $"{_opts.VirusScan.FileStorageBaseUrl.TrimEnd('/')}/api/files/{attachment.FileId}/download";

        var secretKey = _configuration["JwtSettings:SecretKey"];
        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            var grant = FileAccessGrant.Issue(
                secretKey,
                attachment.FileId,
                request.ActorUserId,
                DateTimeOffset.UtcNow.Add(FileAccessGrant.DefaultLifetime));

            downloadUrl = $"{downloadUrl}?{FileAccessGrant.QueryParameterName}={Uri.EscapeDataString(grant)}";
        }

        if (!_opts.Features.EnableVirusScan)
            return new CommonResponse<string> { IsSuccess = true, StatusCode = 200, Data = downloadUrl };

        return attachment.VirusScanStatus switch
        {
            VirusScanStatusEnum.Infected => Fail(451, "File is infected and cannot be downloaded"),
            VirusScanStatusEnum.Clean => new CommonResponse<string> { IsSuccess = true, StatusCode = 200, Data = downloadUrl },

            // GH-790 — Failed KHÔNG còn bị gộp vào 202. "Đang quét, thử lại sau" là lời nói dối khi
            // lượt quét đã hỏng hẳn: client sẽ hỏi lại mãi mãi và không bao giờ nhận được file.
            // 503 nói đúng chuyện đã xảy ra — hệ thống chưa kết luận được, cần người xem.
            VirusScanStatusEnum.Failed => Fail(503,
                "Virus scan could not be completed for this file. Please contact an administrator."),

            // Pending và Scanning đều là "chưa có kết luận" ⇒ 202, thử lại sau là đúng.
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
