using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.CQRS.Command.MaintenanceLogUpdate;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.MaintenanceLogs;

public class MaintenanceLogUpdateCommandHandler : IRequestHandler<MaintenanceLogUpdateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public MaintenanceLogUpdateCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(MaintenanceLogUpdateCommand request, CancellationToken ct)
    {
        var log = await _uow.MaintenanceLogs.GetAllAsync()
            .Include(m => m.Ticket)
            .FirstOrDefaultAsync(m => m.Id == request.LogId && !m.IsDeleted, ct);

        if (log == null)
            return Fail(404, "Không tìm thấy nhật ký bảo trì.");

        var ticket = log.Ticket;

        // KIỂM TRA LOCK LOGIC THEO YÊU CẦU:
        // Khóa nếu Ticket đang ở trạng thái Resolved (đã báo xong, chờ Manager phê duyệt)
        // hoặc đã đóng (ClosedPendingRate, Closed).
        if (ticket.Status == TicketStatusEnum.Resolved ||
            ticket.Status == TicketStatusEnum.ClosedPendingRate ||
            ticket.Status == TicketStatusEnum.Closed)
        {
            return Fail(403, "Nhật ký đã bị khóa. Không thể chỉnh sửa khi Ticket đang chờ phê duyệt hoặc đã hoàn thành.");
        }

        // Kiểm tra quyền sở hữu (chỉ chính Staff đó hoặc Manager/Admin mới được sửa)
        // Lưu ý: Ở đây ta giả định request.StaffId là ID của người đang thực hiện request
        if (log.StaffId != request.StaffId)
        {
            // Nếu không phải chủ nhân, có thể check thêm role Manager/Admin ở tầng Controller hoặc tại đây
            // Để đơn giản và bảo mật theo ý người dùng, ta chỉ cho phép chính chủ sửa trong phase InProgress
            return Fail(403, "Bạn không có quyền chỉnh sửa nhật ký của người khác.");
        }

        // Cập nhật thông tin (chỉ cập nhật những trường được gửi lên)
        if (request.LogType.HasValue)
            log.LogType = request.LogType.Value;
        if (request.Summary != null)
            log.Summary = request.Summary;
        if (request.DiagnosisDetails != null)
            log.DiagnosisDetails = request.DiagnosisDetails;
        if (request.ActionsTaken != null)
            log.ActionsTaken = request.ActionsTaken;
        if (request.DurationMinutes.HasValue)
            log.DurationMinutes = request.DurationMinutes.Value;
        if (request.ResolutionNote != null)
            log.ResolutionNote = request.ResolutionNote;
        if (request.PartsUsed != null)
            log.PartsUsed = request.PartsUsed;
        if (request.RelatedKbArticleIds != null)
            log.RelatedKbArticleIds = request.RelatedKbArticleIds;

        // Cập nhật danh sách File IDs (Metadata)
        if (request.Attachments != null)
            log.AttachmentFileIds = request.Attachments.Select(a => a.FileId).ToList();
        if (request.BeforePhotos != null)
            log.BeforePhotosFileIds = request.BeforePhotos.Select(a => a.FileId).ToList();
        if (request.AfterPhotos != null)
            log.AfterPhotosFileIds = request.AfterPhotos.Select(a => a.FileId).ToList();

        // Xử lý thêm TicketAttachment mới (nếu có)
        await AddNewAttachments(ticket, request.StaffId, request.Attachments, AttachmentSourceEnum.MaintenanceLog, ct);
        await AddNewAttachments(ticket, request.StaffId, request.BeforePhotos, AttachmentSourceEnum.MaintenanceLog, ct);
        await AddNewAttachments(ticket, request.StaffId, request.AfterPhotos, AttachmentSourceEnum.MaintenanceLog, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.StaffId,
            ActorRoleEnum.Staff,
            "Staff",
            ActivityActionEnum.MaintenanceLogged, // Hoặc định nghĩa thêm MaintenanceUpdated
            null,
            $"[Updated] [{log.LogType}] {log.Summary}",
            "Đã cập nhật nhật ký bảo trì.");

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật nhật ký bảo trì thành công.",
            Data = new TicketActionDto
            {
                Id = log.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private async Task AddNewAttachments(TicketEntity ticket, Guid userId, List<MaintenanceAttachmentInput>? inputs, AttachmentSourceEnum source, CancellationToken ct)
    {
        if (inputs == null)
            return;

        // Lấy danh sách FileId đã có trong Ticket để tránh add trùng
        var existingFileIds = await _uow.TicketAttachments.GetAllAsync()
            .Where(a => a.TicketId == ticket.Id)
            .Select(a => a.FileId)
            .ToListAsync(ct);

        foreach (var input in inputs)
        {
            if (!existingFileIds.Contains(input.FileId))
            {
                var attachment = new TicketAttachment
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    UploadedByUserId = userId,
                    FileId = input.FileId,
                    FileName = input.FileName,
                    ContentType = input.ContentType,
                    SizeBytes = input.SizeBytes,
                    Source = source
                };
                await _uow.TicketAttachments.AddAsync(attachment);
            }
        }
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
