using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.SLAs;
using TicketService.Application.CQRS.Query.SLAs;
using TicketService.Application.DTOs.Response.SLAs;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.SLAs;

public sealed class GetSlaNonWorkingPeriodsQueryHandler
    : IRequestHandler<GetSlaNonWorkingPeriodsQuery, CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public GetSlaNonWorkingPeriodsQueryHandler(ITicketUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>> Handle(
        GetSlaNonWorkingPeriodsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _unitOfWork.SlaNonWorkingPeriods.GetAllAsync()
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (request.From.HasValue)
            query = query.Where(x => x.EndDate >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(x => x.StartDate <= request.To.Value);

        var total = await query.CountAsync(cancellationToken);

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist switch-case: startDate (default) | endDate | reason | createdAt.
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "enddate" => descending ? query.OrderByDescending(x => x.EndDate) : query.OrderBy(x => x.EndDate),
            "reason" => descending ? query.OrderByDescending(x => x.Reason) : query.OrderBy(x => x.Reason),
            "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => descending ? query.OrderByDescending(x => x.StartDate) : query.OrderBy(x => x.StartDate),
        };

        var entities = await ordered
            .ThenBy(x => x.EndDate)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = entities.Select(SlaNonWorkingPeriodDto.FromEntity).ToList();

        return new CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "SLA non-working periods retrieved.",
            Data = new PaginationResponse<SlaNonWorkingPeriodDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}

public abstract class SlaNonWorkingPeriodMutationHandler
{
    protected readonly ITicketUnitOfWork UnitOfWork;
    private readonly ISlaBusinessCalendarProvider _calendarProvider;
    private readonly ISlaDeadlineReconciler _deadlineReconciler;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    protected SlaNonWorkingPeriodMutationHandler(
        ITicketUnitOfWork unitOfWork,
        ISlaBusinessCalendarProvider calendarProvider,
        ISlaDeadlineReconciler deadlineReconciler,
        TimeProvider timeProvider)
    {
        UnitOfWork = unitOfWork;
        _calendarProvider = calendarProvider;
        _deadlineReconciler = deadlineReconciler;
        _timeProvider = timeProvider;
    }

    protected CommonResponse<SlaNonWorkingPeriodDto>? ValidateStartDate(DateOnly startDate)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, BusinessTimeZone);
        return startDate < DateOnly.FromDateTime(localNow)
            ? Fail(400, "Start date cannot be in the past.")
            : null;
    }

    protected async Task<bool> OverlapsAsync(DateOnly startDate, DateOnly endDate, Guid? excludingId = null)
    {
        return await UnitOfWork.SlaNonWorkingPeriods.AnyAsync(x =>
            !x.IsDeleted
            && (!excludingId.HasValue || x.Id != excludingId.Value)
            && x.StartDate <= endDate
            && x.EndDate >= startDate);
    }

    protected async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        _calendarProvider.Invalidate();
        await _deadlineReconciler.ReconcileActiveTimersAsync(cancellationToken);
    }

    protected static CommonResponse<SlaNonWorkingPeriodDto> Success(string message, SlaNonWorkingPeriod entity) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = message,
        Data = SlaNonWorkingPeriodDto.FromEntity(entity)
    };

    protected static CommonResponse<SlaNonWorkingPeriodDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}

public sealed class CreateSlaNonWorkingPeriodCommandHandler : SlaNonWorkingPeriodMutationHandler,
    IRequestHandler<CreateSlaNonWorkingPeriodCommand, CommonResponse<SlaNonWorkingPeriodDto>>
{
    public CreateSlaNonWorkingPeriodCommandHandler(ITicketUnitOfWork unitOfWork, ISlaBusinessCalendarProvider calendarProvider,
        ISlaDeadlineReconciler deadlineReconciler, TimeProvider timeProvider)
        : base(unitOfWork, calendarProvider, deadlineReconciler, timeProvider) { }

    public async Task<CommonResponse<SlaNonWorkingPeriodDto>> Handle(CreateSlaNonWorkingPeriodCommand request, CancellationToken ct)
    {
        var invalid = ValidateStartDate(request.StartDate);
        if (invalid is not null)
            return invalid;
        if (await OverlapsAsync(request.StartDate, request.EndDate))
            return Fail(409, "The selected range overlaps an existing non-working period.");

        SlaNonWorkingPeriod? entity = null;
        await UnitOfWork.ExecuteInTransactionAsync(async token =>
        {
            entity = new SlaNonWorkingPeriod
            {
                Id = Guid.NewGuid(),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Reason = request.Reason.Trim(),
                CreatedBy = request.ActorId
            };
            await UnitOfWork.SlaNonWorkingPeriods.AddAsync(entity);
            await ReconcileAsync(token);
        }, ct);

        return Success("SLA non-working period created.", entity!);
    }
}

public sealed class UpdateSlaNonWorkingPeriodCommandHandler : SlaNonWorkingPeriodMutationHandler,
    IRequestHandler<UpdateSlaNonWorkingPeriodCommand, CommonResponse<SlaNonWorkingPeriodDto>>
{
    public UpdateSlaNonWorkingPeriodCommandHandler(ITicketUnitOfWork unitOfWork, ISlaBusinessCalendarProvider calendarProvider,
        ISlaDeadlineReconciler deadlineReconciler, TimeProvider timeProvider)
        : base(unitOfWork, calendarProvider, deadlineReconciler, timeProvider) { }

    public async Task<CommonResponse<SlaNonWorkingPeriodDto>> Handle(UpdateSlaNonWorkingPeriodCommand request, CancellationToken ct)
    {
        var invalid = ValidateStartDate(request.StartDate);
        if (invalid is not null)
            return invalid;
        var entity = await UnitOfWork.SlaNonWorkingPeriods.GetByIdAsync(request.Id);
        if (entity is null || entity.IsDeleted)
            return Fail(404, "SLA non-working period was not found.");
        if (await OverlapsAsync(request.StartDate, request.EndDate, request.Id))
            return Fail(409, "The selected range overlaps an existing non-working period.");

        await UnitOfWork.ExecuteInTransactionAsync(async token =>
        {
            entity.StartDate = request.StartDate;
            entity.EndDate = request.EndDate;
            entity.Reason = request.Reason.Trim();
            UnitOfWork.SlaNonWorkingPeriods.UpdateAsync(entity);
            await ReconcileAsync(token);
        }, ct);

        return Success("SLA non-working period updated.", entity);
    }
}

public sealed class DeleteSlaNonWorkingPeriodCommandHandler : SlaNonWorkingPeriodMutationHandler,
    IRequestHandler<DeleteSlaNonWorkingPeriodCommand, CommonResponse<SlaNonWorkingPeriodDto>>
{
    public DeleteSlaNonWorkingPeriodCommandHandler(ITicketUnitOfWork unitOfWork, ISlaBusinessCalendarProvider calendarProvider,
        ISlaDeadlineReconciler deadlineReconciler, TimeProvider timeProvider)
        : base(unitOfWork, calendarProvider, deadlineReconciler, timeProvider) { }

    public async Task<CommonResponse<SlaNonWorkingPeriodDto>> Handle(DeleteSlaNonWorkingPeriodCommand request, CancellationToken ct)
    {
        var entity = await UnitOfWork.SlaNonWorkingPeriods.GetByIdAsync(request.Id);
        if (entity is null || entity.IsDeleted)
            return Fail(404, "SLA non-working period was not found.");

        await UnitOfWork.ExecuteInTransactionAsync(async token =>
        {
            UnitOfWork.SlaNonWorkingPeriods.DeleteAsync(entity);
            await ReconcileAsync(token);
        }, ct);

        return Success("SLA non-working period deleted.", entity);
    }
}
