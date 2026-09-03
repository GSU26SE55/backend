using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — recompute cascade risk cho asset có Open alert, asset đang decay
/// (Set B), và asset lành ở cùng site với 1 asset đang Open alert (Set C — proximity blind
/// spot fix, xem comment trong <see cref="RecomputeAsync"/>).
///
/// Ngưỡng:
/// - cross từ &lt; 0.7 lên &gt;= 0.7 → publish <see cref="BatteryCascadeRiskHighEvent"/>
///   (TicketService upgrade Priority lên P1, NotificationService notify Manager).
/// - &gt;= 0.5 và &lt; 0.7 → chỉ log (Manager dashboard tự poll score), không auto-upgrade.
///
/// Guard chống spam: chỉ publish khi <c>oldScore &lt; 0.7</c> — score lưu trong DB nên
/// lần scan sau (vẫn &gt;= 0.7) sẽ không re-publish.
/// </summary>
public class CascadeRiskService : ICascadeRiskService
{
    private const decimal HighThreshold = 0.7m;
    private const decimal MediumThreshold = 0.5m;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICascadeRiskCalculator _calculator;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ILogger<CascadeRiskService> _logger;

    public CascadeRiskService(
        IBatteryUnitOfWork unitOfWork,
        ICascadeRiskCalculator calculator,
        IIntegrationEventOutboxWriter outboxWriter,
        ILogger<CascadeRiskService> logger)
    {
        _unitOfWork = unitOfWork;
        _calculator = calculator;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task<CascadeRiskScanResult> RecomputeAsync(
        int batchSize = 200, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var result = new CascadeRiskScanResult();

        // Set A — asset có ít nhất 1 Open alert (battery-level → BatteryAssetId not null): có thể tăng risk.
        var activeIds = await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                && a.Status == AlertStatusEnum.Open
                && a.BatteryAssetId != null)
            .Select(a => a.BatteryAssetId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Set B (Sprint Bonus NS-15 #659, R4 decay) — asset có score > 0 nhưng KHÔNG còn Open alert
        // → recompute để score tự tụt về topology. Nếu không, pin từng chạm 0.8 hiện "High" trên heat
        // map MÃI MÃI dù đã khỏe → Manager mất niềm tin (alert fatigue hệ thống cố tránh).
        var decayIds = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(a => !a.IsDeleted && a.CascadeRiskScore > 0m && !activeIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        // Set C — pin KHÔNG có alert của chính nó, nhưng ở cùng site với 1 pin thuộc Set A.
        // Thiếu bước này thì Rule 2 (Proximity) trong CascadeRiskCalculator không bao giờ có cơ hội
        // chạy cho pin lành: nó chỉ được cộng điểm khi asset đã lọt vào candidateIds, mà trước đây
        // candidateIds chỉ gồm asset TỰ có alert hoặc đang decay — nên pin khỏe mạnh đứng cạnh ổ dịch
        // sẽ mãi mãi kẹt ở score cũ dù về vật lý nó đang chịu rủi ro lan truyền cao nhất.
        var activeSiteIds = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(a => !a.IsDeleted && activeIds.Contains(a.Id) && a.SiteId != null)
            .Select(a => a.SiteId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var siblingOfActiveIds = activeSiteIds.Count == 0
            ? new List<Guid>()
            : await _unitOfWork.BatteryAssets
                .GetAllAsync()
                .Where(a => !a.IsDeleted && a.SiteId != null && activeSiteIds.Contains(a.SiteId!.Value))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

        var candidateIds = activeIds.Concat(decayIds).Concat(siblingOfActiveIds).Distinct().ToList();
        if (candidateIds.Count == 0)
            return result;

        // Sprint Bonus NS-16 (#660, R7) — OrderBy CascadeRiskUpdatedAt (null = chưa tính bao giờ →
        // coi như MinValue, lên đầu) chống starvation khi > batchSize asset: asset lâu chưa tính nhất
        // được ưu tiên, không có asset nào bị bỏ quên qua nhiều tick.
        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(a => !a.IsDeleted && candidateIds.Contains(a.Id))
            .OrderBy(a => a.CascadeRiskUpdatedAt ?? DateTime.MinValue)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var asset in assets)
        {
            var oldScore = asset.CascadeRiskScore;
            var newScore = await _calculator.CalculateAsync(asset.Id, cancellationToken);

            asset.CascadeRiskScore = newScore;
            asset.CascadeRiskUpdatedAt = now;
            _unitOfWork.BatteryAssets.UpdateAsync(asset);
            result.Scanned++;

            if (newScore >= HighThreshold && oldScore < HighThreshold)
            {
                // Lấy ticket Open liên quan (nếu có) để TicketService upgrade Priority.
                var relatedTicketId = await _unitOfWork.Alerts
                    .GetAllAsync()
                    .Where(a => !a.IsDeleted
                        && a.BatteryAssetId == asset.Id
                        && a.Status == AlertStatusEnum.Open
                        && a.TicketId != null)
                    .OrderByDescending(a => a.DetectedAt)
                    .Select(a => a.TicketId)
                    .FirstOrDefaultAsync(cancellationToken);

                var evt = new BatteryCascadeRiskHighEvent(
                    BatteryAssetId: asset.Id,
                    SiteId: asset.SiteId,
                    CustomerId: asset.CustomerId,
                    AssetSerialNumber: asset.SerialNumber,
                    CascadeRiskScore: newScore,
                    RelatedTicketId: relatedTicketId,
                    DetectedAt: now);

                await _outboxWriter.WriteAsync(evt, cancellationToken);
                result.HighRisk++;

                _logger.LogWarning(
                    "Cascade risk HIGH: asset {AssetId} ({Serial}) score {Score} (was {Old}) — published BatteryCascadeRiskHighEvent",
                    asset.Id, asset.SerialNumber, newScore, oldScore);
            }
            else if (newScore >= MediumThreshold && oldScore < MediumThreshold)
            {
                result.MediumRisk++;
                _logger.LogInformation(
                    "Cascade risk MEDIUM: asset {AssetId} ({Serial}) score {Score} — Manager dashboard review",
                    asset.Id, asset.SerialNumber, newScore);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
