using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.TicketKbReferences;

public class AddTicketKbReferenceCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    public Guid TicketId { get; set; }
    public Guid KbArticleId { get; set; }
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (KbArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "KbArticleId", Detail = "KbArticleId không hợp lệ." });

        if (!Enum.IsDefined(typeof(KbReferenceTypeEnum), ReferenceType))
            response.ListErrors.Add(new Errors { Field = "ReferenceType", Detail = "Loại tham chiếu không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
