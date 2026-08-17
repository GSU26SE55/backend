using BatteryService.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// GH-verify-sensor-grpc — gRPC client đọc sensor pin từ BatteryService (BatteryInternal.GetSensorSnapshot).
/// Nội bộ solar-net, KHÔNG JWT. Fail/asset không có reading → trả null (verify không chặn).
/// </summary>
public class BatterySensorGrpcClient : IBatterySensorClient
{
    private readonly BatteryInternal.BatteryInternalClient _client;
    private readonly ILogger<BatterySensorGrpcClient> _logger;
    private readonly int _timeoutSeconds;

    public BatterySensorGrpcClient(
        BatteryInternal.BatteryInternalClient client,
        ILogger<BatterySensorGrpcClient> logger,
        int timeoutSeconds)
    {
        _client = client;
        _logger = logger;
        _timeoutSeconds = timeoutSeconds;
    }

    public async Task<TicketSensorSnapshotDto?> GetSnapshotAsync(Guid assetId, CancellationToken ct)
    {
        if (assetId == Guid.Empty)
            return null;

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            var resp = await _client.GetSensorSnapshotAsync(
                new SensorSnapshotRequest { AssetId = assetId.ToString() },
                deadline: deadline,
                cancellationToken: ct);

            // Asset không tồn tại HOẶC chưa có reading → verify bỏ qua sensor.
            if (!resp.Found)
                return null;

            return new TicketSensorSnapshotDto
            {
                SohPercent = resp.SohPercent,
                Voltage = resp.Voltage,
                Current = resp.Current,
                Temperature = resp.Temperature,
                SocPercent = resp.SocPercent,
                HasActiveAlert = resp.HasActiveAlert
            };
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Đọc sensor pin {AssetId} lỗi gRPC ({Status}) — verify bỏ qua sensor.", assetId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Đọc sensor pin {AssetId} lỗi — verify bỏ qua sensor.", assetId);
            return null;
        }
    }
}
