using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Query.Dashboard;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Dashboard;

public class GetBatteryDashboardStatsQueryHandler
    : IRequestHandler<GetBatteryDashboardStatsQuery, CommonResponse<BatteryDashboardStatsDto>>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly AnomalyEngineOptions _options;
    private readonly IBatteryCurrentUserService _currentUser;

    public GetBatteryDashboardStatsQueryHandler(
        IBatteryUnitOfWork uow,
        IOptions<AnomalyEngineOptions> options,
        IBatteryCurrentUserService currentUser)
    {
        _uow = uow;
        _options = options.Value;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<BatteryDashboardStatsDto>> Handle(
        GetBatteryDashboardStatsQuery request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var offlineCutoff = now - TimeSpan.FromMinutes(_options.OfflineThresholdMinutes);
        var siteId = request.SiteId;

        // ===== GH-774: phân quyền =====
        //
        // Controller chỉ có `[Authorize]`, trong khi chính tài liệu của endpoint ghi "trả tổng hợp
        // toàn bộ system (yêu cầu role Admin/Manager)". Đo được lúc chạy thật: cả Manager, Staff và
        // Customer đều nhận 200 với totalAssets=10 — tức toàn bộ số liệu đội tàu, cảnh báo và phân
        // phối SOH của mọi khách hàng bị lộ cho bất kỳ ai đăng nhập.
        //
        // Đặt ở handler chứ không phải controller: đây là giới hạn theo DỮ LIỆU, phải nằm cùng chỗ
        // truy dữ liệu — thêm attribute ở controller sẽ không chặn được ca truyền siteId của người
        // khác. Chính sách lấy nguyên từ BatteryTenantScopeHelper (spec §34.10.6) để không trôi
        // khỏi đường SSE.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUser.UserId, _currentUser.Roles);

        if (!siteId.HasValue)
        {
            // Tổng hợp TOÀN HỆ THỐNG: chỉ Admin/Manager. Staff tuy được xem mọi asset (§34.10.6)
            // nhưng con số gộp toàn đội tàu là thông tin điều hành, không phải thứ cần để xử lý
            // ticket — và tài liệu của endpoint đã nói vậy từ đầu.
            if (!_currentUser.Roles.Any(r =>
                    r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                    || r.Equals("Manager", StringComparison.OrdinalIgnoreCase)))
            {
                return new CommonResponse<BatteryDashboardStatsDto>
                {
                    IsSuccess = false,
                    StatusCode = 403,
                    Message = "Only Admin/Manager can view system-wide statistics. Pass siteId to view by site."
                };
            }
        }
        else if (!await BatteryTenantAccessGuard.CanAccessSiteAsync(_uow, siteId.Value, scope, ct))
        {
            // Site của người khác: trả 404 chứ không 403 — 403 xác nhận site đó có thật, biến
            // endpoint thành công cụ dò xem khách hàng nào đang tồn tại. Khớp quy ước GH-722.
            return new CommonResponse<BatteryDashboardStatsDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Site not found."
            };
        }

        // ===== Asset queries =====
        var assetsQuery = _uow.BatteryAssets.GetAllAsync().Where(a => !a.IsDeleted);
        if (siteId.HasValue)
            assetsQuery = assetsQuery.Where(a => a.SiteId == siteId.Value);

        var totalAssets = await assetsQuery.CountAsync(ct);
        var activeAssets = await assetsQuery.CountAsync(a => a.Status == BatteryStatusEnum.Active, ct);
        var offlineAssets = await assetsQuery.CountAsync(
            a => a.LastSensorReadingAt == null || a.LastSensorReadingAt < offlineCutoff, ct);

        // Asset status distribution
        var statusGroups = await assetsQuery
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var statusDistribution = statusGroups
            .Select(g => new AssetStatusBucketDto
            {
                Status = g.Status,
                StatusName = g.Status.ToString(),
                Count = g.Count
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        // Chemistry distribution (join BatteryType)
        var chemistryGroups = await assetsQuery
            .Join(_uow.BatteryTypes.GetAllAsync().Where(t => !t.IsDeleted),
                a => a.BatteryTypeId,
                t => t.Id,
                (a, t) => new { t.Chemistry })
            .GroupBy(x => x.Chemistry)
            .Select(g => new { Chemistry = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var chemistryDistribution = chemistryGroups
            .Select(g => new ChemistryBucketDto
            {
                Chemistry = g.Chemistry,
                ChemistryName = g.Chemistry.ToString(),
                AssetCount = g.Count
            })
            .OrderByDescending(g => g.AssetCount)
            .ToList();

        // ===== Alert queries =====
        var alertsQueryBase = _uow.Alerts.GetAllAsync().Where(a => !a.IsDeleted);
        if (siteId.HasValue)
            alertsQueryBase = alertsQueryBase.Where(a =>
                a.SiteId == siteId.Value ||
                (a.BatteryAsset != null && a.BatteryAsset.SiteId == siteId.Value));

        var openAlertsQuery = alertsQueryBase.Where(a => a.Status == AlertStatusEnum.Open);
        var openAlerts = await openAlertsQuery.CountAsync(ct);
        var openCritical = await openAlertsQuery.CountAsync(a => a.Severity == AlertSeverityEnum.Critical, ct);
        var openWarning = await openAlertsQuery.CountAsync(a => a.Severity == AlertSeverityEnum.Warning, ct);
        var openInfo = await openAlertsQuery.CountAsync(a => a.Severity == AlertSeverityEnum.Info, ct);

        var severityBreakdown = new AlertSeverityBreakdownDto
        {
            Critical = openCritical,
            Warning = openWarning,
            Info = openInfo
        };

        // Open alerts by anomaly type
        var byTypeGroups = await openAlertsQuery
            .GroupBy(a => a.AnomalyType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var openAlertsByType = byTypeGroups
            .Select(g => new AlertByTypeDto
            {
                AnomalyType = g.Type,
                AnomalyName = g.Type.ToString(),
                Count = g.Count
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        // Alert trend 7 days
        var trendFrom = now.Date.AddDays(-6);
        var trendRaw = await alertsQueryBase
            .Where(a => a.DetectedAt >= trendFrom)
            .Select(a => new { Date = a.DetectedAt.Date, a.Severity })
            .ToListAsync(ct);
        var trendByDay = trendRaw
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        var alertTrend = new List<DailyTrendPointDto>();
        for (var i = 0; i < 7; i++)
        {
            var day = trendFrom.AddDays(i);
            var entries = trendByDay.TryGetValue(day, out var list) ? list : new();
            alertTrend.Add(new DailyTrendPointDto
            {
                Date = DateOnly.FromDateTime(day),
                Critical = entries.Count(e => e.Severity == AlertSeverityEnum.Critical),
                Warning = entries.Count(e => e.Severity == AlertSeverityEnum.Warning),
                Info = entries.Count(e => e.Severity == AlertSeverityEnum.Info),
                Total = entries.Count
            });
        }

        // Top alerting assets (30 ngày)
        var topFrom = now.AddDays(-30);
        var topAssetsRaw = await alertsQueryBase
            .Where(a => a.DetectedAt >= topFrom && a.BatteryAssetId != null)
            .GroupBy(a => a.BatteryAssetId!.Value)
            .Select(g => new
            {
                AssetId = g.Key,
                AlertCount = g.Count(),
                CriticalCount = g.Count(a => a.Severity == AlertSeverityEnum.Critical)
            })
            .OrderByDescending(g => g.AlertCount)
            .Take(5)
            .ToListAsync(ct);
        var topAssetIds = topAssetsRaw.Select(x => x.AssetId).ToList();
        var topAssetMeta = await _uow.BatteryAssets.GetAllAsync()
            .Where(a => topAssetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.SerialNumber })
            .ToListAsync(ct);
        var topMetaMap = topAssetMeta.ToDictionary(x => x.Id, x => x.SerialNumber);
        var topAlertingAssets = topAssetsRaw
            .Select(x => new TopAlertingAssetDto
            {
                BatteryAssetId = x.AssetId,
                SerialNumber = topMetaMap.GetValueOrDefault(x.AssetId) ?? "(unknown)",
                AlertCount = x.AlertCount,
                CriticalCount = x.CriticalCount
            })
            .ToList();

        // ===== SOH bucket — dùng LastSensorReadingAt per asset =====
        var assetIds = await assetsQuery.Select(a => a.Id).ToListAsync(ct);
        var sohQuery = _uow.SensorReadings.GetAllAsync()
            .Where(r => assetIds.Contains(r.BatteryAssetId))
            .GroupBy(r => r.BatteryAssetId)
            .Select(g => new
            {
                AssetId = g.Key,
                LatestSoh = g.OrderByDescending(r => r.Time).Select(r => (decimal?)r.SohPercent).FirstOrDefault()
            });
        var sohRows = await sohQuery.ToListAsync(ct);
        var sohBucket = new SohBucketDto();
        foreach (var id in assetIds)
        {
            var match = sohRows.FirstOrDefault(s => s.AssetId == id);
            var soh = match?.LatestSoh;
            if (soh is null)
                sohBucket.Unknown++;
            else if (soh >= 90m)
                sohBucket.Healthy++;
            else if (soh >= 80m)
                sohBucket.Normal++;
            else if (soh >= 75m)
                sohBucket.Warning++;
            else
                sohBucket.Eol++;
        }

        // ===== Sensor aggregate 24h =====
        var sensorFrom = now.AddHours(-24);
        var sensorBase = _uow.SensorReadings.GetAllAsync()
            .Where(r => r.Time >= sensorFrom && assetIds.Contains(r.BatteryAssetId));
        var sensorAgg = await sensorBase
            .GroupBy(r => 1)
            .Select(g => new
            {
                AvgVoltage = (decimal?)g.Average(r => r.Voltage),
                AvgCurrent = (decimal?)g.Average(r => r.Current),
                AvgTemperature = (decimal?)g.Average(r => r.Temperature),
                AvgSoc = (decimal?)g.Average(r => r.SocPercent),
                AvgSoh = (decimal?)g.Average(r => r.SohPercent),
                Count = g.Count()
            })
            .FirstOrDefaultAsync(ct);
        var sensorAggregate = sensorAgg is null
            ? new SensorAggregateDto()
            : new SensorAggregateDto
            {
                AvgVoltage = Round(sensorAgg.AvgVoltage),
                AvgCurrent = Round(sensorAgg.AvgCurrent),
                AvgTemperature = Round(sensorAgg.AvgTemperature),
                AvgSoc = Round(sensorAgg.AvgSoc),
                AvgSoh = Round(sensorAgg.AvgSoh),
                ReadingsCount = sensorAgg.Count
            };

        // ===== Ambient trend 24h (theo giờ) =====
        var ambientBase = _uow.AmbientReadings.GetAllAsync().Where(r => r.Time >= sensorFrom);
        if (siteId.HasValue)
            ambientBase = ambientBase.Where(r => r.SiteId == siteId.Value);
        var ambientRaw = await ambientBase
            .Select(r => new { r.Time, r.AmbientTemperature, r.Humidity, r.SolarIrradiance })
            .ToListAsync(ct);
        var ambientHourly = ambientRaw
            .GroupBy(r => new DateTime(r.Time.Year, r.Time.Month, r.Time.Day, r.Time.Hour, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new AmbientTrendPointDto
            {
                HourUtc = g.Key,
                AvgTemperature = Round(g.Average(r => (decimal?)r.AmbientTemperature)),
                AvgHumidity = Round(g.Where(r => r.Humidity.HasValue).Select(r => r.Humidity).DefaultIfEmpty().Average()),
                AvgSolarIrradiance = Round(g.Where(r => r.SolarIrradiance.HasValue).Select(r => r.SolarIrradiance).DefaultIfEmpty().Average())
            })
            .ToList();

        // ===== Environmental incidents =====
        var incidentsQueryBase = _uow.EnvironmentalIncidents.GetAllAsync().Where(e => !e.IsDeleted);
        if (siteId.HasValue)
            incidentsQueryBase = incidentsQueryBase.Where(e => e.SiteId == siteId.Value);
        var openIncidents = await incidentsQueryBase
            .CountAsync(e => e.Status == EnvironmentalIncidentStatusEnum.Open, ct);
        var activeIncidentTypeGroups = await incidentsQueryBase
            .Where(e => e.Status == EnvironmentalIncidentStatusEnum.Open
                     || e.Status == EnvironmentalIncidentStatusEnum.Acknowledged)
            .GroupBy(e => e.IncidentType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var incidentsByType = activeIncidentTypeGroups
            .Select(g => new EnvironmentalIncidentByTypeDto
            {
                IncidentType = g.Type,
                IncidentName = g.Type.ToString(),
                Count = g.Count
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        // ===== Sites =====
        var sitesQuery = _uow.Sites.GetAllAsync().Where(s => !s.IsDeleted);
        if (siteId.HasValue)
            sitesQuery = sitesQuery.Where(s => s.Id == siteId.Value);
        var sites = await sitesQuery.CountAsync(ct);

        return new CommonResponse<BatteryDashboardStatsDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BatteryDashboardStatsDto
            {
                TotalAssets = totalAssets,
                ActiveAssets = activeAssets,
                OfflineAssets = offlineAssets,
                OpenAlerts = openAlerts,
                OpenAlertsCritical = openCritical,
                OpenAlertsWarning = openWarning,
                OpenEnvironmentalIncidents = openIncidents,
                Sites = sites,
                AssetStatusDistribution = statusDistribution,
                ChemistryDistribution = chemistryDistribution,
                SohDistribution = sohBucket,
                AlertSeverityBreakdown = severityBreakdown,
                OpenAlertsByType = openAlertsByType,
                AlertTrend7Days = alertTrend,
                AmbientTrend24Hours = ambientHourly,
                SensorAggregate24Hours = sensorAggregate,
                TopAlertingAssets = topAlertingAssets,
                EnvironmentalIncidentsByType = incidentsByType
            }
        };
    }

    private static decimal? Round(decimal? value)
        => value.HasValue ? Math.Round(value.Value, 2) : null;
}
