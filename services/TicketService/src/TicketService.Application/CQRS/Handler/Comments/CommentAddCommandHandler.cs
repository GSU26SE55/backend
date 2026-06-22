using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.CommentAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Comments;

public class CommentAddCommandHandler : IRequestHandler<CommentAddCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketCommentRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<CommentAddCommandHandler> _logger;

    public CommentAddCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ITicketCommentRealtimeNotifier realtimeNotifier,
        ILogger<CommentAddCommandHandler> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(CommentAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            AuthorUserId = request.UserId,
            AuthorRole = request.UserRole,
            AuthorDisplayName = request.UserDisplayName,
            Body = request.Body,
            IsInternal = request.IsInternal,
            AttachmentFileIds = request.Attachments?.Select(a => a.FileId).ToList() ?? new List<Guid>()
        };

        await _uow.TicketComments.AddAsync(comment);

        if (request.Attachments != null && request.Attachments.Any())
        {
            foreach (var att in request.Attachments)
            {
                var attachment = new TicketAttachment
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    UploadedByUserId = request.UserId,
                    FileId = att.FileId,
                    FileName = att.FileName,
                    ContentType = att.ContentType,
                    SizeBytes = att.SizeBytes,
                    Source = request.UserRole == ActorRoleEnum.Customer
                        ? AttachmentSourceEnum.CustomerSubmission
                        : AttachmentSourceEnum.StaffWork
                };
                await _uow.TicketAttachments.AddAsync(attachment);
            }
        }

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.Commented,
            null,
            request.IsInternal ? "[Nội bộ]" : "[Công khai]",
            $"Đã thêm bình luận: {request.Body[..Math.Min(request.Body.Length, 50)]}...");

        await _uow.SaveChangesAsync(ct);

        // Broadcast comment via SignalR
        try
        {
            var commentDto = new TicketCommentDTO
            {
                Id = comment.Id.ToString(),
                TicketId = comment.TicketId.ToString(),
                AuthorUserId = comment.AuthorUserId.ToString(),
                AuthorRole = comment.AuthorRole,
                AuthorDisplayName = comment.AuthorDisplayName,
                Body = comment.Body,
                IsInternal = comment.IsInternal,
                AttachmentFileIds = comment.AttachmentFileIds.Select(id => id.ToString()).ToList(),
                CreatedAt = comment.CreatedAt
            };
            await _realtimeNotifier.NotifyCommentAddedAsync(commentDto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CommentAdd] Failed to broadcast CommentAdded SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm bình luận thành công.",
            Data = new TicketActionDTO
            {
                Id = comment.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
