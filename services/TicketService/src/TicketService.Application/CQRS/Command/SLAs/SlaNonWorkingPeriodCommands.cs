using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.SLAs;

namespace TicketService.Application.CQRS.Command.SLAs;

public abstract class SlaNonWorkingPeriodWriteCommand : IRequest<CommonResponse<SlaNonWorkingPeriodDto>>, IValidatable<CommonResponse<SlaNonWorkingPeriodDto>>
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ActorId { get; set; }

    public Task<CommonResponse<SlaNonWorkingPeriodDto>> ValidateAsync()
    {
        var response = new CommonResponse<SlaNonWorkingPeriodDto>();
        if (StartDate == default)
            response.ListErrors.Add(new Errors { Field = nameof(StartDate), Detail = "Start date is required." });
        if (EndDate == default)
            response.ListErrors.Add(new Errors { Field = nameof(EndDate), Detail = "End date is required." });
        if (StartDate != default && EndDate != default && StartDate > EndDate)
            response.ListErrors.Add(new Errors { Field = nameof(EndDate), Detail = "End date must be on or after start date." });
        if (string.IsNullOrWhiteSpace(Reason))
            response.ListErrors.Add(new Errors { Field = nameof(Reason), Detail = "Reason is required." });
        else if (Reason.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = nameof(Reason), Detail = "Reason cannot exceed 500 characters." });
        if (ActorId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(ActorId), Detail = "Authenticated actor is required." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}

public sealed class CreateSlaNonWorkingPeriodCommand : SlaNonWorkingPeriodWriteCommand;

public sealed class UpdateSlaNonWorkingPeriodCommand : SlaNonWorkingPeriodWriteCommand
{
    public Guid Id { get; set; }
}

public sealed record DeleteSlaNonWorkingPeriodCommand(Guid Id, Guid ActorId)
    : IRequest<CommonResponse<SlaNonWorkingPeriodDto>>;
