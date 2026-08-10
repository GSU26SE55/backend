using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Query.IotDevice;

/// <summary>
/// IOT3-58 — lịch sử heartbeat của một IoT device.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint này từng được XML doc của <c>AdminIotDevicesController</c> nhắc tới như thể đã tồn tại
/// ("dùng <c>GET .../{id}/heartbeats</c> (Sprint IoT-2)") trong khi nó chưa hề được viết. Bảng dữ
/// liệu thì đã có và đã được ghi từ IoT-1 — chỉ thiếu đường đọc ra.
/// </para>
/// <para>
/// Phân trang theo CON TRỎ, không offset. Xem <see cref="IotDeviceHeartbeatListDto"/>.
/// </para>
/// </remarks>
public class GetIotDeviceHeartbeatsQuery
    : IRequest<CommonResponse<IotDeviceHeartbeatListDto>>,
      IValidatable<CommonResponse<IotDeviceHeartbeatListDto>>
{
    public const int MaxLimit = 1000;

    /// <summary>Lấy từ route — query string + body không bind để tránh nhầm lẫn nguồn.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    /// <summary>Lọc từ thời điểm (UTC, bao gồm).</summary>
    public DateTime? From { get; set; }

    /// <summary>Lọc đến thời điểm (UTC, bao gồm).</summary>
    public DateTime? To { get; set; }

    public int Limit { get; set; } = 100;

    /// <summary>
    /// Mốc thời gian của bản ghi CUỐI trang trước. Kết quả sắp xếp MỚI TRƯỚC, nên trang kế lấy
    /// những bản ghi có <c>Time</c> NHỎ HƠN con trỏ.
    /// </summary>
    public DateTime? Cursor { get; set; }

    public Task<CommonResponse<IotDeviceHeartbeatListDto>> ValidateAsync()
    {
        var response = new CommonResponse<IotDeviceHeartbeatListDto>();

        if (DeviceId == Guid.Empty)
            AddError(response, nameof(DeviceId), "Id thiết bị là bắt buộc.");

        if (Limit < 1 || Limit > MaxLimit)
            AddError(response, nameof(Limit), $"Limit phải nằm trong khoảng 1-{MaxLimit}.");

        if (From.HasValue && To.HasValue && ToUtc(From.Value) > ToUtc(To.Value))
        {
            response.IsSuccess = false;
            // Lỗi liên trường → 422; KHÔNG ghi đè 400 (lỗi định dạng từng trường ưu tiên hơn).
            if (response.StatusCode != 400) response.StatusCode = 422;
            response.Message = "Dữ liệu không hợp lệ.";
            response.ListErrors.Add(new Errors
            {
                Field = nameof(To),
                Detail = "Thời điểm kết thúc phải lớn hơn hoặc bằng thời điểm bắt đầu."
            });
        }

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<IotDeviceHeartbeatListDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
