using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint 7 B4 (§31.7) — recompute cascade risk cho asset có Open alert.
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

        // Asset có ít nhất 1 Open alert (battery-level → BatteryAssetId not null).
        var assetIds = await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                && a.Status == AlertStatusEnum.Open
                && a.BatteryAssetId != null)
            .Select(a => a.BatteryAssetId!.Value)
            .Distinct()
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (assetIds.Count == 0)
            return result;

        foreach (var assetId in assetIds)
        {
            var asset = await _unitOfWork.BatteryAssets.GetByIdAsync(assetId);
            if (asset is null || asset.IsDeleted)
                continue;

            var oldScore = asset.CascadeRiskScore;
            var newScore = await _calculator.CalculateAsync(assetId, cancellationToken);

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
                        && a.BatteryAssetId == assetId
                        && a.Status == AlertStatusEnum.Open
                        && a.TicketId != null)
                    .OrderByDescending(a => a.DetectedAt)
                    .Select(a => a.TicketId)
                    .FirstOrDefaultAsync(cancellationToken);

                var evt = new BatteryCascadeRiskHighEvent(
                    BatteryAssetId: assetId,
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
                    assetId, asset.SerialNumber, newScore, oldScore);
            }
            else if (newScore >= MediumThreshold && oldScore < MediumThreshold)
            {
                result.MediumRisk++;
                _logger.LogInformation(
                    "Cascade risk MEDIUM: asset {AssetId} ({Serial}) score {Score} — Manager dashboard review",
                    assetId, asset.SerialNumber, newScore);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
