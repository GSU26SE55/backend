using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Implements.Services;

public sealed class SlaBusinessCalendarProvider : ISlaBusinessCalendarProvider
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private IReadOnlyList<SlaNonWorkingPeriod>? _periods;

    public SlaBusinessCalendarProvider(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public bool IsNonWorkingDate(DateOnly localDate)
    {
        _periods ??= _unitOfWork.SlaNonWorkingPeriods.GetAllAsync()
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.StartDate)
            .ToList();

        return _periods.Any(x => x.StartDate <= localDate && x.EndDate >= localDate);
    }

    public void Invalidate()
    {
        _periods = null;
    }
}
