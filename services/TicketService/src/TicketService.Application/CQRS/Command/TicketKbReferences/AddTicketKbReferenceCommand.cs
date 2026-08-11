using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.TicketKbReferences;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.TicketKbReferences;

public class AddTicketKbReferenceCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public Guid TicketId { get; set; }
    public Guid KbArticleId { get; set; }
    public KbReferenceTypeEnum ReferenceType { get; set; }
    /// <summary>
    /// Note.
    /// </summary>
    public string? Note { get; set; }
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (KbArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "KbArticleId", Detail = "Invalid KbArticleId." });

        if (!Enum.IsDefined(typeof(KbReferenceTypeEnum), ReferenceType))
            response.ListErrors.Add(new Errors { Field = "ReferenceType", Detail = "Invalid reference type." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
