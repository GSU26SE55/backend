using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace TicketService.Application.CQRS.Command.TicketKbReferences;

public class RemoveTicketKbReferenceCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    [JsonIgnore]
    public Guid ReferenceId { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (ReferenceId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ReferenceId", Detail = "ID tham chiếu không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
