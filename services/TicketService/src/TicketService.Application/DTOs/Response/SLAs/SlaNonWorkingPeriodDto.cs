using TicketService.Domain.Entities;

namespace TicketService.Application.DTOs.Response.SLAs;

public sealed class SlaNonWorkingPeriodDto
{
    public string Id { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static SlaNonWorkingPeriodDto FromEntity(SlaNonWorkingPeriod entity) => new()
    {
        Id = entity.Id.ToString(),
        StartDate = entity.StartDate,
        EndDate = entity.EndDate,
        Reason = entity.Reason,
        CreatedAt = entity.CreatedAt,
        CreatedBy = entity.CreatedBy?.ToString(),
        UpdatedAt = entity.UpdatedAt
    };
}
