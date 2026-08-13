using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Logic AI verify dùng chung cho consumer (async) + re-verify thủ công (Manager).
/// Chấm điểm thật/rác + dò trùng cùng pin → update ticket. Human-in-the-loop, KHÔNG chặn.
/// </summary>
public class TicketVerifyRunner : ITicketVerifyRunner
{
    // Ticket còn "đang mở" (chưa kết thúc) — dùng để dò candidate trùng.
    private static readonly TicketStatusEnum[] OpenStatuses =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Pending,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.Request,
        TicketStatusEnum.ReAssign
    };

    private readonly ITicketUnitOfWork _uow;
    private readonly IAiTicketVerifyClient _verifyClient;
    private readonly IBatterySensorClient _sensorClient;
    private readonly TicketAiOptions _options;
    private readonly ILogger<TicketVerifyRunner> _logger;

    public TicketVerifyRunner(
        ITicketUnitOfWork uow,
        IAiTicketVerifyClient verifyClient,
        IBatterySensorClient sensorClient,
        IOptions<TicketAiOptions> options,
        ILogger<TicketVerifyRunner> logger)
    {
        _uow = uow;
        _verifyClient = verifyClient;
        _sensorClient = sensorClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == ticketId && !t.IsDeleted, ct);

        if (ticket is null)
        {
            _logger.LogWarning("TicketVerify: ticket {TicketId} không tồn tại — bỏ qua.", ticketId);
            return;
        }

        // Chỉ verify ticket còn chờ verify (Pending). Đã xử lý → idempotent skip.
        if (ticket.AiVerifyStatus != TicketVerifyStatusEnum.Pending)
            return;

        if (!_options.Enabled)
        {
            ticket.AiVerifyStatus = TicketVerifyStatusEnum.Skipped;
            _uow.Tickets.UpdateAsync(ticket);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        // Dò candidate trùng: ticket khác cùng pin, cùng customer, còn mở, chưa merge.
        var candidates = new List<DuplicateCandidateDto>();
        if (ticket.BatteryAssetId != Guid.Empty)
        {
            var raw = await _uow.Tickets.GetAllAsync()
                .AsNoTracking()
                .Where(t => !t.IsDeleted
                    && t.Id != ticket.Id
                    && t.BatteryAssetId == ticket.BatteryAssetId
                    && t.CustomerId == ticket.CustomerId
                    && t.MergedIntoTicketId == null
                    && OpenStatuses.Contains(t.Status))
                .OrderByDescending(t => t.CreatedAt)
                .Take(_options.MaxDuplicateCandidates)
                .Select(t => new { t.Id, t.Description, t.Category })
                .ToListAsync(ct);

            candidates = raw.Select(t => new DuplicateCandidateDto
            {
                TicketId = t.Id.ToString(),
                Description = t.Description,
                Category = (int)t.Category
            }).ToList();
        }

        // GH-verify-sensor-grpc — đọc sensor pin thật (gRPC nội bộ, không JWT) để AI đối chiếu
        // mô tả với thực tế. Fail/pin chưa có reading → null → verify vẫn chạy bằng heuristic text.
        TicketSensorSnapshotDto? sensor = null;
        if (ticket.BatteryAssetId != Guid.Empty)
        {
            sensor = await _sensorClient.GetSnapshotAsync(ticket.BatteryAssetId, ct);
            if (sensor is not null)
                _logger.LogInformation(
                    "TicketVerify: {Code} — đọc sensor pin thật (SOH={Soh:F0}% Temp={Temp:F0}°C Alert={Alert}).",
                    ticket.Code, sensor.SohPercent, sensor.Temperature, sensor.HasActiveAlert);
        }

        var result = await _verifyClient.VerifyAsync(
            ticket.Title,
            ticket.Description,
            ticket.DetectedAt,
            (int)ticket.Category,
            sensor,
            candidates,
            ct);

        if (result is null)
        {
            ticket.AiVerifyStatus = TicketVerifyStatusEnum.Skipped;
            _logger.LogWarning("TicketVerify: {Code} — AI không phản hồi, đánh dấu Skipped.", ticket.Code);
        }
        else
        {
            ticket.AiVerifyStatus = string.Equals(result.Verdict, "suspicious", StringComparison.OrdinalIgnoreCase)
                ? TicketVerifyStatusEnum.Suspicious
                : TicketVerifyStatusEnum.Legitimate;
            ticket.AiVerifyScore = result.Score;
            ticket.AiVerifyReason = result.Reason;

            if (!string.IsNullOrEmpty(result.DuplicateOfTicketId)
                && Guid.TryParse(result.DuplicateOfTicketId, out var dupId)
                && dupId != Guid.Empty)
            {
                ticket.SuspectedDuplicateOfTicketId = dupId;
                ticket.DuplicateReason = result.DuplicateReason;
                _logger.LogWarning(
                    "TicketVerify: {Code} NGHI TRÙNG ticket {DupId} ({Reason}).",
                    ticket.Code, dupId, result.DuplicateReason);
            }

            if (ticket.AiVerifyStatus == TicketVerifyStatusEnum.Suspicious)
                _logger.LogWarning(
                    "TicketVerify: {Code} NGHI RÁC (score={Score:F2}): {Reason}",
                    ticket.Code, result.Score, result.Reason);
            else
                _logger.LogInformation(
                    "TicketVerify: {Code} hợp lệ (score={Score:F2}).", ticket.Code, result.Score);
        }

        _uow.Tickets.UpdateAsync(ticket);
        await _uow.SaveChangesAsync(ct);
    }
}
