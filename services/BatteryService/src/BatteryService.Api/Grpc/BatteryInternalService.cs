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

        return new SensorSnapshotResponse
        {
            Found = true,
            Serial = dto.SerialNumber ?? string.Empty,
            SohPercent = (double)(soh ?? 0m),
            Voltage = (double)(dto.Voltage ?? 0m),
            Current = (double)(dto.Current ?? 0m),
            Temperature = (double)(dto.Temperature ?? 0m),
            SocPercent = (double)(dto.SocPercent ?? 0m),
            HasActiveAlert = dto.ActiveAlerts > 0
        };
    }
}
