using TicketService.Application.Common.Models;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Gọi AI module (gRPC VerifyTicket) để chấm điểm ticket thật/rác + dò trùng mô tả.
/// Human-in-the-loop: AI chỉ gắn nhãn, Manager quyết. Fail/exception → trả null (verify skip, KHÔNG chặn).
/// </summary>
public interface IAiTicketVerifyClient
{
    Task<AiVerifyResult?> VerifyAsync(
        string title,
        string description,
        DateTime? detectedAt,
        int category,
        TicketSensorSnapshotDto? sensor,
        IReadOnlyList<DuplicateCandidateDto> candidates,
        bool isMachineWritten,
        CancellationToken ct);
}
