using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.MaintenanceLogs;

public class MaintenanceLogAddCommandHandler : IRequestHandler<MaintenanceLogAddCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public MaintenanceLogAddCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(MaintenanceLogAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        // KIỂM TRA LOCK LOGIC: Không cho thêm log khi đã báo Resolved hoặc đã Closed
        if (ticket.Status == TicketStatusEnum.Resolved ||
            ticket.Status == TicketStatusEnum.ClosedPendingRate ||
            ticket.Status == TicketStatusEnum.Closed)
        {
            return Fail(403, "Ticket đã ở trạng thái chờ phê duyệt hoặc đã hoàn thành. Không thể thêm nhật ký mới.");
        }

        // KIỂM TRA LOG ĐANG CHẠY (IN-PROGRESS):
        // Nếu log mới định add mà chưa có CompletedAt (đang bắt đầu làm)
        // thì check xem ticket này đã có cái nào chưa xong không.
        if (request.CompletedAt == null)
        {
            var activeLog = await _uow.MaintenanceLogs.GetAllAsync()
                .AnyAsync(m => m.TicketId == ticket.Id && m.CompletedAt == null && !m.IsDeleted, ct);

            if (activeLog)
            {
                return Fail(409, "Đang có một nhật ký bảo trì chưa hoàn thành cho Ticket này. Vui lòng hoàn thành nhật ký cũ trước khi bắt đầu cái mới.");
            }
        }

        var log = new MaintenanceLog
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            StaffId = request.StaffId,
            LogType = request.LogType,
            Summary = request.Summary,
            DiagnosisDetails = request.DiagnosisDetails,
            ActionsTaken = request.ActionsTaken,
            DurationMinutes = request.DurationMinutes,
            ResolutionNote = request.ResolutionNote,
            StartedAt = request.StartedAt,
            CompletedAt = request.CompletedAt,
            PartsUsed = request.PartsUsed,
            AttachmentFileIds = request.Attachments?.Select(a => a.FileId).ToList() ?? new List<Guid>(),
            BeforePhotosFileIds = request.BeforePhotos?.Select(a => a.FileId).ToList() ?? new List<Guid>(),
            AfterPhotosFileIds = request.AfterPhotos?.Select(a => a.FileId).ToList() ?? new List<Guid>(),
            RelatedKbArticleIds = request.RelatedKbArticleIds ?? new List<Guid>(),
            CheckInLatitude = request.CheckInLatitude,
            CheckInLongitude = request.CheckInLongitude,
            CheckInAt = request.CheckInAt
        };

        await _uow.MaintenanceLogs.AddAsync(log);

        // Process attachments as TicketAttachment records
        await AddAttachments(ticket, request.StaffId, request.Attachments, AttachmentSourceEnum.MaintenanceLog);
        await AddAttachments(ticket, request.StaffId, request.BeforePhotos, AttachmentSourceEnum.MaintenanceLog);
        await AddAttachments(ticket, request.StaffId, request.AfterPhotos, AttachmentSourceEnum.MaintenanceLog);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.StaffId,
            ActorRoleEnum.Staff,
            "Staff",
            ActivityActionEnum.MaintenanceLogged,
            null,
            $"[{request.LogType}] {request.Summary}",
            "Đã thêm nhật ký bảo trì.");

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm nhật ký bảo trì thành công.",
            Data = new TicketActionDTO
            {
                Id = log.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private async Task AddAttachments(TicketEntity ticket, Guid userId, List<MaintenanceAttachmentInput>? inputs, AttachmentSourceEnum source)
    {
        if (inputs == null)
            return;

        foreach (var input in inputs)
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
