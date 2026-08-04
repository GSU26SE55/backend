using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public sealed class TicketReprioritizeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    public TicketPriorityEnum Priority { get; set; }
    public string Reason { get; set; } = string.Empty;
    [JsonIgnore] public Guid ManagerId { get; set; }
    [JsonIgnore] public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(TicketId), Detail = "TicketId không hợp lệ." });
        if (!Enum.IsDefined(Priority))
            response.ListErrors.Add(new Errors { Field = nameof(Priority), Detail = "Priority không hợp lệ." });
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length > 1000)
            response.ListErrors.Add(new Errors { Field = nameof(Reason), Detail = "Lý do là bắt buộc và tối đa 1000 ký tự." });
        if (response.ListErrors.Count > 0)
        { response.IsSuccess = false; response.StatusCode = 400; response.Message = "Dữ liệu đầu vào không hợp lệ."; }
        return Task.FromResult(response);
    }
}
