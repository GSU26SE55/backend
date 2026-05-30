using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketCreateCommandHandler : IRequestHandler<TicketCreateCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly IActivityLogger _activityLogger;

    public TicketCreateCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(TicketCreateCommand request, CancellationToken ct)
    {
        var code = await _codeGenerator.GenerateAsync();

        var ticket = new TicketEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            CustomerId = request.CustomerId,
            BatteryAssetId = request.BatteryAssetId ?? Guid.Empty,
            Status = TicketStatusEnum.New,
            Origin = TicketOriginEnum.ManualByCustomer,
            ReopenCount = 0,
            IsIncident = false
        };

        await _uow.Tickets.AddAsync(ticket);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.CustomerId,
            ActorRoleEnum.Customer,
            "Customer",
            ActivityActionEnum.Created);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Ticket created successfully.",
            Data = new TicketActionDto
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message, string field = "Ticket")
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = new List<Errors>
            {
                new Errors { Field = field, Detail = message }
            }
        };
    }
}
