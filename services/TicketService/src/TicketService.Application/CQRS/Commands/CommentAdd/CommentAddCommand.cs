using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Commands.CommentAdd;

public record CommentAddCommand(
    Guid TicketId,
    Guid UserId,
    ActorRoleEnum UserRole,
    string UserDisplayName,
    string Body,
    bool IsInternal,
    List<CommentAttachmentInput>? Attachments = null
) : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung bình luận không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}

public record CommentAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes
);
