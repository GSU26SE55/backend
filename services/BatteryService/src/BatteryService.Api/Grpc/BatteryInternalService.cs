using System.Globalization;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.Interfaces;
using BatteryService.Grpc;
using global::Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Api.Grpc;

/// <summary>
/// GH-verify-sensor-grpc — gRPC server impl của BatteryInternal.GetSensorSnapshot.
/// Service-to-service (TicketService verify), nội bộ solar-net, KHÔNG JWT.
/// Tái dùng <see cref="GetBatteryAssetRealtimeQuery"/> để lấy snapshot mới nhất của pin.
/// </summary>
public class BatteryInternalService : BatteryInternal.BatteryInternalBase
{
    private readonly IMediator _mediator;
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ILogger<BatteryInternalService> _logger;

    public BatteryInternalService(
        IMediator mediator,
        IBatteryUnitOfWork unitOfWork,
        ILogger<BatteryInternalService> logger)
    {
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override async Task<SensorSnapshotResponse> GetSensorSnapshot(
        SensorSnapshotRequest request, ServerCallContext context)
    {
        // asset_id không hợp lệ → found=false (verify bỏ qua sensor, không chặn).
        if (!Guid.TryParse(request.AssetId, out var assetId) || assetId == Guid.Empty)
            return new SensorSnapshotResponse { Found = false };

        // Có `detected_at` → chụp tình trạng pin LÚC XẢY RA sự cố, không phải lúc gọi hàm này.
        // Customer thường mở app hàng giờ sau khi thấy vấn đề; đọc realtime khi đó thì pin đã
        // nguội/đã sạc lại, AI kết luận "sensor không thấy bất thường" và trừ điểm một báo cáo
        // hoàn toàn đúng.
        if (!string.IsNullOrWhiteSpace(request.DetectedAt)
            && DateTime.TryParse(request.DetectedAt, CultureInfo.InvariantCulture,
                                 DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                 out var detectedAt))
        {
            var snapshot = await BuildSnapshotAroundAsync(assetId, detectedAt, context.CancellationToken);
            if (snapshot is not null) return snapshot;
            // Không có reading nào trong cửa sổ → rơi xuống đường realtime bên dưới thay vì trả
            // found=false: thà đối chiếu bằng số đo mới nhất còn hơn bỏ hẳn tín hiệu sensor.
        }

        var result = await _mediator.Send(
            new GetBatteryAssetRealtimeQuery { Id = assetId },
            context.CancellationToken);

        // Asset không tồn tại HOẶC chưa có reading (Time null) → found=false.
        if (result is null || !result.IsSuccess || result.Data is null || result.Data.Time is null)
            return new SensorSnapshotResponse { Found = false };

        var dto = result.Data;

        // Simulator stream SOH thưa (nhiều packet gần nhất soh_percent = null) → snapshot mất SOH.
        // Fallback: lấy SOH gần nhất KHÁC null để AI đối chiếu ngưỡng EOL 80% đúng thực tế.
        var soh = dto.SohPercent;
        if (soh is null)
        {
            soh = await _unitOfWork.SensorReadings
                .GetAllAsync()
                .AsNoTracking()
                .Where(r => r.BatteryAssetId == assetId && r.SohPercent != null)
                .OrderByDescending(r => r.Time)
                .Select(r => r.SohPercent)
                .FirstOrDefaultAsync(context.CancellationToken);
        }

        var (tMax, tMin, socWarn, sohWarn) = await GetThresholdsAsync(assetId, context.CancellationToken);

        return new SensorSnapshotResponse
        {
            Found = true,
            Serial = dto.SerialNumber ?? string.Empty,
            SohPercent = (double)(soh ?? 0m),
            Voltage = (double)(dto.Voltage ?? 0m),
            Current = (double)(dto.Current ?? 0m),
            Temperature = (double)(dto.Temperature ?? 0m),
            SocPercent = (double)(dto.SocPercent ?? 0m),
            HasActiveAlert = dto.ActiveAlerts > 0,
            TemperatureMax = tMax,
            TemperatureMin = tMin,
            SocWarningThreshold = socWarn,
            SohWarningThreshold = sohWarn
        };
    }

    /// <summary>
    /// Ngưỡng của loại pin, gắn vào snapshot để AI verify chấm bằng ĐÚNG giới hạn mà
    /// <c>AnomalyRules</c> đã áp — thay vì hardcode một bộ số riêng và bất đồng với backend.
    /// </summary>
    /// <remarks>
    /// Không có config → trả 0 hết; phía AI hiểu 0 là "không biết ngưỡng" và bỏ qua luật đó
    /// thay vì đoán. Im lặng vẫn hơn xác nhận một sự cố không tồn tại.
    /// </remarks>
    private async Task<(double TempMax, double TempMin, double SocWarn, double SohWarn)>
        GetThresholdsAsync(Guid assetId, CancellationToken ct)
    {
        var row = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(a => a.Id == assetId && !a.IsDeleted)
            .Join(_unitOfWork.ThresholdConfigs.GetAllAsync().AsNoTracking()
                      .Where(t => t.IsActive && !t.IsDeleted),
                  a => a.BatteryTypeId,
                  t => t.BatteryTypeId,
                  (a, t) => new
                  {
                      t.TemperatureMax,
                      t.TemperatureMin,
                      t.SocWarningThreshold,
                      t.SohWarningThreshold
                  })
            .FirstOrDefaultAsync(ct);

        if (row is null) return (0, 0, 0, 0);
        return ((double)row.TemperatureMax,
                (double)row.TemperatureMin,
                (double)row.SocWarningThreshold,
                (double)(row.SohWarningThreshold ?? 0m));
    }

    /// <summary>Nửa bề rộng cửa sổ quanh `detected_at` khi dựng snapshot theo mốc khai báo.</summary>
    /// <remarks>
    /// Thiết bị gửi mỗi 10 giây nên ±2 phút cho ~24 số đo — dư so với ngưỡng chống nhiễu 5 lần
    /// mà backend dùng để kết luận có sự cố, đồng thời đủ hẹp để không kéo vào một sự kiện khác
    /// trong ngày. Hẹp hơn (±1') thì người nhớ lệch vài phút là cửa sổ rỗng, mà nhớ lệch vài
    /// phút là chuyện bình thường khi khai báo sự cố.
    /// </remarks>
    private static readonly TimeSpan SnapshotWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Dựng snapshot từ các số đo quanh <paramref name="detectedAt"/>, lấy giá trị CỰC ĐOAN
    /// thay vì trung bình hay dòng gần nhất.
    /// </summary>
    /// <remarks>
    /// Sự cố là một đỉnh điểm, không phải trạng thái nền: pin chạm 70°C trong nửa phút rồi hạ
    /// về 35°C vẫn là quá nhiệt. Lấy trung bình cửa sổ sẽ làm nhòe đúng cái đỉnh đó và AI kết
    /// luận "không thấy bất thường" — nên mỗi trường lấy hướng nguy hiểm nhất: nhiệt cao nhất,
    /// SOC và SOH thấp nhất, dòng có trị tuyệt đối lớn nhất.
    ///
    /// Trả null khi cửa sổ không có số đo nào; caller sẽ lùi về đường realtime.
    /// </remarks>
    private async Task<SensorSnapshotResponse?> BuildSnapshotAroundAsync(
        Guid assetId, DateTime detectedAt, CancellationToken ct)
    {
        var from = detectedAt - SnapshotWindow;
        var to = detectedAt + SnapshotWindow;

        var readings = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(r => r.BatteryAssetId == assetId && r.Time >= from && r.Time <= to)
            .ToListAsync(ct);

        if (readings.Count == 0) return null;

        var asset = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(a => a.Id == assetId && !a.IsDeleted)
            .Select(a => new { a.SerialNumber })
            .FirstOrDefaultAsync(ct);

        // SOH thưa trong stream (nhiều packet để null) — bỏ null trước khi lấy min, nếu không
        // `Min()` trên tập toàn null trả về null và AI mất hẳn tín hiệu EOL.
        var sohValues = readings.Where(r => r.SohPercent != null)
                                .Select(r => r.SohPercent!.Value)
                                .ToList();

        // Alert đang mở tại thời điểm khai báo — không phải "đang mở lúc này". Một sự cố đã được
        // xử lý xong vẫn phải tính là bằng chứng cho ticket khai báo về chính nó.
        var hadActiveAlert = await _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .AnyAsync(a => !a.IsDeleted
                           && a.BatteryAssetId == assetId
                           && a.DetectedAt <= to
                           && (a.ResolvedAt == null || a.ResolvedAt >= from), ct);

        var (tMax, tMin, socWarn, sohWarn) = await GetThresholdsAsync(assetId, ct);

        return new SensorSnapshotResponse
        {
            Found = true,
            Serial = asset?.SerialNumber ?? string.Empty,
            SohPercent = sohValues.Count > 0 ? (double)sohValues.Min() : 0,
            Voltage = (double)readings.Max(r => r.Voltage),
            Current = (double)readings.OrderByDescending(r => Math.Abs(r.Current)).First().Current,
            Temperature = (double)readings.Max(r => r.Temperature),
            SocPercent = (double)readings.Min(r => r.SocPercent),
            HasActiveAlert = hadActiveAlert,
            TemperatureMax = tMax,
            TemperatureMin = tMin,
            SocWarningThreshold = socWarn,
            SohWarningThreshold = sohWarn
        };
    }
}
