using BatteryService.Application.Ai;
using BatteryService.Application.Common.Models;
using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

/// <summary>
/// Chạy lại <c>Prescribe</c> ở chế độ đầy đủ cho một alert, theo yêu cầu thủ công của người dùng.
/// </summary>
public class RegenerateAiPrescriptionCommandHandler
    : IRequestHandler<RegenerateAiPrescriptionCommand, CommonResponse<AiPrescriptionDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IAiPrescriptionClient _prescriptionClient;
    private readonly IAiHealthClient _healthClient;
    private readonly ILogger<RegenerateAiPrescriptionCommandHandler> _logger;

    public RegenerateAiPrescriptionCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IAiPrescriptionClient prescriptionClient,
        IAiHealthClient healthClient,
        ILogger<RegenerateAiPrescriptionCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _prescriptionClient = prescriptionClient;
        _healthClient = healthClient;
        _logger = logger;
    }

    public async Task<CommonResponse<AiPrescriptionDto>> Handle(
        RegenerateAiPrescriptionCommand request, CancellationToken cancellationToken)
    {
        // KHÔNG AsNoTracking: prescription mới sẽ được gắn lên alert bên dưới, nếu không
        // POST /prescription-feedback (đọc alert.AiPrescriptionId) sẽ trả 409 dù kỹ thuật
        // viên vừa nhận được đơn — vòng phản hồi đứt đúng tại chỗ nối hai endpoint.
        var alert = await _unitOfWork.Alerts.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AlertId && !a.IsDeleted, cancellationToken);

        if (alert is null)
            return Fail(404, "Không tìm thấy alert.");

        if (alert.BatteryAssetId is not Guid assetId)
            return Fail(409, "Alert này ở cấp site, không gắn với pin nào nên không kê đơn được.");

        var asset = await _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.Id == assetId && !a.IsDeleted)
            .Select(a => new
            {
                a.Id,
                NominalVoltage = a.BatteryType!.NominalVoltage,
                NominalCapacityAh = a.BatteryType.NominalCapacityAh,
                Chemistry = a.BatteryType.Chemistry,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
            return Fail(404, "Không tìm thấy pin của alert này.");

        var packConfig = BuildPackConfig(asset.NominalVoltage, asset.NominalCapacityAh, asset.Chemistry);

        // Quét dư rồi mới lọc, giống job nền: một số đo bất khả thi không được làm hỏng cả cửa sổ.
        var scannedDesc = await _unitOfWork.SensorReadings.GetAllAsync()
            .AsNoTracking()
            // KHÔNG hardcode SourceType. Dữ liệu production đến từ IoT gateway
            // (source_type=2), không phải Bms — lọc theo Bms sẽ khiến MỌI pin chạy gateway
            // không bao giờ kê được đơn thủ công, dù job nền vẫn dự đoán cho chúng bình
            // thường. Phải dùng ĐÚNG bộ lọc của SohPredictionBackgroundService, nếu không
            // hai đường đọc cùng một bảng lại thấy hai tập dữ liệu khác nhau.
            .Where(r => r.BatteryAssetId == assetId
                        && (r.SensorSourceCode == null
                            || r.SensorSourceCode == ""
                            || r.SensorSourceCode == "primary"))
            .OrderByDescending(r => r.Time)
            .Take(AiOptions.WindowSize * 2)
            .ToListAsync(cancellationToken);

        if (scannedDesc.Count < AiOptions.WindowSize)
            return Fail(409, $"Pin chưa đủ {AiOptions.WindowSize} số đo để AI kê đơn.");

        var scanned = scannedDesc.OrderBy(r => r.Time).ToList();

        // soc_mode quyết định gửi 4 hay 6 cột — đọc từ AI, không suy từ chemistry.
        var health = await _healthClient.GetHealthAsync(cancellationToken);
        var socMode = health?.SocModeFor(packConfig.Chemistry);
        var allowDerived = socMode switch
        {
            "cycle" => true,
            "window" or "unknown" => false,
            _ => string.Equals(packConfig.Chemistry, "LFP", StringComparison.OrdinalIgnoreCase),
        };

        var checkRows = scanned
            .Select(r => allowDerived && r.CycleCount.HasValue
                ? new[]
                {
                    (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                    0d, r.CycleCount.Value, (double)r.SocPercent,
                }
                : new[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature })
            .ToList();

        var filtered = AiReadingWindowFilter.Filter(checkRows, packConfig);
        if (filtered.AcceptedCount < AiOptions.WindowSize)
            return Fail(409, "Không đủ số đo hợp lệ trong dải AI chấp nhận để kê đơn.");

        var window = filtered.AcceptedIndices
            .Skip(filtered.AcceptedCount - AiOptions.WindowSize)
            .Select(i => scanned[i])
            .ToList();

        // Bộ soc_mode="cycle" từ chối thẳng payload 4 cột — dừng trước khi gọi cho khỏi phí.
        if (allowDerived && !window.All(r => r.CycleCount.HasValue))
            return Fail(409, "Cửa sổ thiếu cycle_count, mà model của pin này bắt buộc phải có.");

        var t0 = window[0].Time;
        var readings = window
            .Select(r =>
            {
                var seconds = (r.Time - t0).TotalSeconds;
                return allowDerived && r.CycleCount.HasValue
                    ? new double[]
                    {
                        (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                        seconds, r.CycleCount.Value, (double)r.SocPercent,
                    }
                    : new double[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature, seconds };
            })
            .ToList();

        var resolved = await _unitOfWork.Alerts.GetAllAsync()
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.BatteryAssetId == assetId && a.ResolvedAt != null)
            .OrderByDescending(a => a.ResolvedAt)
            .Take(5)
            .Select(a => new { a.ResolvedAt, a.AnomalyType, a.Severity })
            .ToListAsync(cancellationToken);

        var context = new AiPrescriptionContext(
            AgeCycles: window[^1].CycleCount,
            LastMaintenanceDate: resolved.Count > 0
                ? resolved.Max(a => a.ResolvedAt)!.Value.ToString("yyyy-MM-dd")
                : null,
            // CŨ → MỚI: AI chỉ lấy 5 phần tử cuối.
            TicketHistory: resolved
                .OrderBy(a => a.ResolvedAt)
                .Select(a => $"{a.ResolvedAt!.Value:yyyy-MM-dd}: {a.AnomalyType} ({a.Severity}) — đã xử lý")
                .ToList());

        var result = await _prescriptionClient.PrescribeAsync(
            assetId.ToString(), readings, enrich: true, packConfig, cancellationToken,
            context, request.Agentic);

        if (result is null)
        {
            _logger.LogWarning("AI không kê được đơn cho alert {AlertId}.", request.AlertId);
            return Fail(503, "AI không phản hồi. Thử lại sau.");
        }

        // Gắn id mới lên alert: đơn vừa kê LÀ đơn hiện hành của alert này, nên phản hồi của
        // kỹ thuật viên phải đi về đúng nó. Chỉ ghi khi AI thật sự cấp id (enrich=false vẫn
        // có id, nhưng history store hỏng thì trả rỗng — khi đó giữ nguyên id cũ còn hơn xoá).
        if (!string.IsNullOrWhiteSpace(result.PrescriptionId))
        {
            alert.AiPrescriptionId = result.PrescriptionId;
            _unitOfWork.Alerts.UpdateAsync(alert);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new CommonResponse<AiPrescriptionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new AiPrescriptionDto
            {
                Prescription = result.Prescription,
                ActionSteps = result.ActionSteps,
                PpeRequired = result.PpeRequired,
                SopReferences = result.SopReferences,
                SafetyWarnings = result.SafetyWarnings,
                EscalationConditions = result.EscalationConditions,
                HumanVerificationRequired = result.HumanVerificationRequired,
                Enriched = result.Enriched,
                LlmProvider = result.LlmProvider,
                Blocked = result.Blocked,
                Cached = result.Cached,
                PrescriptionId = result.PrescriptionId,
            },
        };
    }

    /// <summary>Giống hệt cách job nền tính — pin 12.8V LFP (cell 3.2V) → 4S.</summary>
    private static AiPackConfig BuildPackConfig(
        decimal nominalVoltage, decimal nominalCapacityAh, BatteryChemistryEnum chemistry)
    {
        var (cellNominal, aiChemistry) = chemistry switch
        {
            BatteryChemistryEnum.LiFePO4 => (3.2m, "LFP"),
            BatteryChemistryEnum.Nmc => (3.7m, "NMC"),
            BatteryChemistryEnum.Nca => (3.6m, "NMC"),
            BatteryChemistryEnum.Lco => (3.7m, "NMC"),
            _ => (3.7m, (string?)null),
        };
        var nSeries = Math.Max(1, (int)Math.Round(nominalVoltage / cellNominal));
        return new AiPackConfig(nSeries, aiChemistry, (double)nominalCapacityAh);
    }

    private static CommonResponse<AiPrescriptionDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
