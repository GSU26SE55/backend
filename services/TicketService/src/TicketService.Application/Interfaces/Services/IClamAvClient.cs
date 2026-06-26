using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IClamAvClient
{
    /// <summary>
    /// Upload <paramref name="fileStream"/> lên ClamAV REST /scan endpoint.
    /// Returns Clean, Infected, hoặc Failed (khi ClamAV không phản hồi hoặc lỗi parse).
    /// </summary>
    Task<VirusScanStatusEnum> ScanAsync(Stream fileStream, string fileName, CancellationToken ct);
}
