using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.TicketKbReferences;

namespace TicketService.Application.CQRS.Command.TicketKbReferences;

public class RemoveTicketKbReferenceCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    /// <summary>
    /// Reference id.
    /// </summary>
    [JsonIgnore]
    public Guid ReferenceId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (ReferenceId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ReferenceId", Detail = "Invalid reference ID." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
