using BatteryService.Application.CQRS.Query.Alert;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Alert;

public class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, CommonResponse<PaginationResponse<AlertDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetAlertsQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<AlertDto>>> Handle(GetAlertsQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — Customer chỉ thấy alert của asset/site thuộc mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<PaginationResponse<AlertDto>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Unable to determine the current user."
            };
        }

        var customerAccounts = _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AsNoTracking()
            .Where(account => !account.IsDeleted);

        var query = _unitOfWork.Alerts
            .GetAllAsync()
            .AsNoTracking()
            .Include(alert => alert.BatteryAsset)
            .Include(alert => alert.Site)
            // Màn hình "Device alerts" hiển thị mã/tên thiết bị thay cho serial pin.
            .Include(alert => alert.IotDevice)
            // Màn hình "Environmental alerts" hiển thị loại sự cố thật (Gas leak, Flood…) cho dòng
            // alert bản sao — bản thân alert không mang thông tin đó.
            .Include(alert => alert.EnvironmentalIncident)
            .Where(alert => !alert.IsDeleted);

        // Alert có thể gắn asset HOẶC site (cả hai đều nullable) — phải phủ cả hai đường.
        if (scope.IsCustomerScoped)
        {
            query = query.Where(alert =>
                (alert.BatteryAsset != null && alert.BatteryAsset.CustomerId == scope.CustomerId)
                || (alert.Site != null && alert.Site.CustomerId == scope.CustomerId));
        }

        if (request.BatteryAssetId.HasValue)
            query = query.Where(alert => alert.BatteryAssetId == request.BatteryAssetId.Value);

        if (request.SiteId.HasValue)
            query = query.Where(alert => alert.SiteId == request.SiteId.Value);

        if (request.Severity.HasValue)
            query = query.Where(alert => alert.Severity == request.Severity.Value);

        if (request.Status.HasValue)
            query = query.Where(alert => alert.Status == request.Status.Value);
        else if (request.ExcludeMerged)
            query = query.Where(alert => alert.Status != Domain.Enums.AlertStatusEnum.Merged);

        if (request.AnomalyType.HasValue)
            query = query.Where(alert => alert.AnomalyType == request.AnomalyType.Value);

        // "Battery alerts" là alert gắn với MỘT viên pin. Mọi alert cấp site — mirror của
        // EnvironmentalIncident, và ngưỡng môi trường (nhiệt độ/độ ẩm/khí gas) — không có pin nào
        // để hiện serial, nên bị loại khỏi đây.
        //
        // Lọc theo CHÍNH quan hệ "có gắn pin hay không", không liệt kê từng AnomalyType: bản cũ
        // chỉ trừ đúng EnvironmentalIncident, nên khi thêm loại ngưỡng môi trường mới
        // (HighGasConcentration #18) nó lại lọt vào danh sách pin dưới dạng dòng "Site level"
        // không có serial. Liệt kê theo loại là lỗi hẹn giờ — cứ thêm loại là quên cập nhật.
        // Chỉ áp dụng khi client không tự chỉ định AnomalyType.
        if (request.ExcludeEnvironmentalIncidents && !request.AnomalyType.HasValue)
            query = query.Where(alert => alert.BatteryAssetId != null);

        // Mặt đối: bảng "Vượt ngưỡng" của màn hình Environmental alerts. Phải trừ thêm hai loại
        // alert gateway — chúng cũng không gắn pin, nên nếu chỉ xét "không có pin" thì alert thiết
        // bị sẽ hiện lẫn ở đây và bị đếm hai lần trên sidebar.
        // GIỮ LẠI alert bản sao của EnvironmentalIncident: mỗi incident đều ghi kèm một dòng ở đây,
        // nên bảng `alerts` đã chứa TRỌN hai thứ mà màn hình Environmental cần hiện (sự cố do
        // firmware báo + vượt ngưỡng do backend phát hiện). Nhờ vậy màn hình dùng được MỘT nguồn,
        // MỘT phân trang — thay vì ghép hai endpoint đã phân trang riêng, vốn không phân trang đúng
        // được. Dòng bản sao lấy tên và số đo từ chính incident (xem IncidentType/IncidentTypeName).
        if (request.SiteLevelOnly && !request.AnomalyType.HasValue)
            query = query.Where(alert => alert.BatteryAssetId == null
                && alert.AnomalyType != Domain.Enums.AnomalyTypeEnum.DeviceOffline
                && alert.AnomalyType != Domain.Enums.AnomalyTypeEnum.IotDataIntegrityViolation);

        // Alert cấp thiết bị IoT gắn IotDeviceId chứ không gắn pin — màn hình "Device alerts"
        // lấy đúng hai loại này, "Battery alerts" loại chúng ra, nên hai danh sách rời nhau và
        // không màn nào còn dòng trống serial. Lọc ở BE (không phải client) để totalItems và
        // phân trang khớp với những gì hiển thị. Cùng khuôn với ExcludeEnvironmentalIncidents:
        // chỉ áp dụng khi client không tự chỉ định AnomalyType.
        if (!request.AnomalyType.HasValue)
        {
            if (request.IotOnly)
            {
                query = query.Where(alert =>
                    alert.AnomalyType == Domain.Enums.AnomalyTypeEnum.DeviceOffline
                    || alert.AnomalyType == Domain.Enums.AnomalyTypeEnum.IotDataIntegrityViolation);
            }
            else if (request.ExcludeIotDeviceAlerts)
            {
                query = query.Where(alert =>
                    alert.AnomalyType != Domain.Enums.AnomalyTypeEnum.DeviceOffline
                    && alert.AnomalyType != Domain.Enums.AnomalyTypeEnum.IotDataIntegrityViolation);
            }
        }

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(alert => alert.DetectedAt >= from);
        }

        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(alert => alert.DetectedAt <= to);
        }

        // Alert gắn asset HOẶC site — chủ sở hữu lấy từ đường nào có mặt, cùng hai đường mà
        // tenant scope ở trên đã dùng. Join sau khi lọc, trước khi phân trang (join 1:1 nên
        // total không đổi).
        var joined = from alert in query
                     join account in customerAccounts
                         on (alert.BatteryAsset != null
                                ? alert.BatteryAsset.CustomerId
                                : (alert.Site != null ? alert.Site.CustomerId : Guid.Empty))
                         equals account.Id into accountJoin
                     from account in accountJoin.DefaultIfEmpty()
                     select new { alert, account };

        var page = await joined
            .OrderByDescending(x => x.alert.DetectedAt)
            .ThenBy(x => x.alert.Id) // tie-breaker cố định — pagination ổn định
            .Select(x => new AlertDto
            {
                Id = x.alert.Id.ToString(),
                BatteryAssetId = x.alert.BatteryAssetId.HasValue
                    ? x.alert.BatteryAssetId.Value.ToString()
                    : string.Empty,
                IotDeviceId = x.alert.IotDeviceId.HasValue ? x.alert.IotDeviceId.Value.ToString() : null,
                // Sprint Bonus NS-21 (#661) — null cho alert cấp pin, GUID cho alert cấp site.
                SiteId = x.alert.SiteId.HasValue ? x.alert.SiteId.Value.ToString() : null,
                // Alert cấp site (ambient/env) không có BatteryAsset → serial rỗng thay vì null.
                BatterySerialNumber = x.alert.BatteryAsset != null
                    ? x.alert.BatteryAsset.SerialNumber ?? string.Empty
                    : string.Empty,
                // Alert cấp thiết bị không có BatteryAsset → hai cột dưới là thứ duy nhất định
                // danh được sự cố. Rỗng cho alert pin/site, giống quy ước của BatterySerialNumber.
                // Navigation nullable (alert pin/site không gắn device) — EF dịch cả cụm sang
                // SQL LEFT JOIN nên null-safe ở runtime, nhưng dùng `?.` để compiler khỏi cảnh
                // báo CS8602 và ý định đọc ra rõ.
                IotDeviceCode = x.alert.IotDevice != null ? x.alert.IotDevice.DeviceCode : string.Empty,
                IotDeviceName = x.alert.IotDevice != null ? x.alert.IotDevice.DisplayName : string.Empty,
                SiteName = x.alert.Site != null ? x.alert.Site.Name : string.Empty,
                CustomerName = x.account != null ? x.account.FullName : string.Empty,
                AnomalyType = x.alert.AnomalyType,
                EnvironmentalIncidentId = x.alert.EnvironmentalIncidentId.HasValue
                    ? x.alert.EnvironmentalIncidentId.Value.ToString()
                    : null,
                IncidentType = x.alert.EnvironmentalIncident != null
                    ? x.alert.EnvironmentalIncident.IncidentType
                    : null,
                Severity = x.alert.Severity,
                ThresholdValue = x.alert.ThresholdValue,
                ActualValue = x.alert.ActualValue,
                Unit = x.alert.Unit,
                DetectedAt = x.alert.DetectedAt,
                Status = x.alert.Status,
                TicketId = x.alert.TicketId.HasValue ? x.alert.TicketId.Value.ToString() : null,
                AcknowledgedByUserId = x.alert.AcknowledgedByUserId.HasValue ? x.alert.AcknowledgedByUserId.Value.ToString() : null,
                AcknowledgedAt = x.alert.AcknowledgedAt,
                ResolvedAt = x.alert.ResolvedAt,
                DedupWindowEndUtc = x.alert.DedupWindowEndUtc,
                AiPrescriptionId = x.alert.AiPrescriptionId,
                CreatedAt = x.alert.CreatedAt
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<AlertDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
