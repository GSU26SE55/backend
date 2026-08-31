using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Handler.EnvironmentalIncident;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-806 — hàng rào site phải thật sự được GỌI trong hai handler, không chỉ tồn tại.
/// </summary>
/// <remarks>
/// <see cref="IotSiteAccessGuardTests"/> kiểm luật; lớp này kiểm việc nối dây. Guard đúng mà handler
/// quên gọi thì mọi test luật vẫn xanh trong khi lỗ hổng còn nguyên — đúng loại sai sót mà một bộ
/// test "đầy đủ" hay bỏ lọt.
/// </remarks>
public class IotSiteScopeEnforcementTests
{
    private static readonly Guid SiteA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
    private static readonly Guid SiteB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000022");
    private static readonly Guid Ghost = Guid.Parse("cccccccc-0000-0000-0000-000000000033");

    private static Site Site(Guid id) => new()
    {
        Id = id,
        Name = "Site " + id.ToString()[..4],
        Address = "somewhere",
        Status = SiteStatusEnum.Active,
    };

    // ── Ambient ──────────────────────────────────────────────────────────────

    private static BatchIngestAmbientReadingsCommand AmbientFor(Guid siteId, Guid? deviceSite) => new()
    {
        AuthenticatedDeviceSiteId = deviceSite,
        Items =
        [
            new AmbientReadingItem
            {
                Time = DateTime.UtcNow,
                SiteId = siteId,
                AmbientTemperature = 30,
                Humidity = 55,
                SolarIrradiance = 700,
                Source = AmbientReadingSourceEnum.IotSensor,
            }
        ],
    };

    private static BatchIngestAmbientReadingsCommandHandler AmbientHandler(params Site[] sites)
        => new(new MockUnitOfWorkBuilder().WithSites(sites).Build(),
               Microsoft.Extensions.Options.Options.Create(
                   new BatteryService.Application.Anomaly.AnomalyEngineOptions()));

    [Fact]
    public async Task Ambient_CrossSiteWrite_IsRejectedWith403()
    {
        var result = await AmbientHandler(Site(SiteA), Site(SiteB))
            .Handle(AmbientFor(SiteB, deviceSite: SiteA), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Data.Should().Be(0, "không dòng nào được ghi khi bị chặn");
    }

    [Fact]
    public async Task Ambient_SameSiteWrite_StillSucceeds()
    {
        // Chiều dương bắt buộc phải có: siết mà chặn luôn đường hợp lệ thì telemetry môi trường chết
        // hẳn, và triệu chứng sẽ là "không có dữ liệu" chứ không phải một lỗi rõ ràng.
        var result = await AmbientHandler(Site(SiteA))
            .Handle(AmbientFor(SiteA, deviceSite: SiteA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Ambient_UnknownSite_IsA404_NotADatabaseError()
    {
        var result = await AmbientHandler(Site(SiteA))
            .Handle(AmbientFor(Ghost, deviceSite: null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    // ── Environmental incident ───────────────────────────────────────────────

    private static ReportEnvironmentalIncidentCommand IncidentFor(Guid siteId, Guid? deviceSite) => new()
    {
        SiteId = siteId,
        AuthenticatedDeviceSiteId = deviceSite,
        IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
        Severity = AlertSeverityEnum.Critical,
        DetectedAt = DateTime.UtcNow,
        ReportedBy = "device",
    };

    private static ReportEnvironmentalIncidentCommandHandler IncidentHandler(params Site[] sites)
        => new(new MockUnitOfWorkBuilder().WithSites(sites).Build(),
               new NoopOutboxWriter(),
               new NoopEnvironmentalMetrics());

    [Fact]
    public async Task Incident_CrossSiteReport_IsRejectedWith403()
    {
        // Sự cố giả cho tenant khác là kịch bản tệ nhất: nó kéo theo alert và ticket ưu tiên cao.
        var result = await IncidentHandler(Site(SiteA), Site(SiteB))
            .Handle(IncidentFor(SiteB, deviceSite: SiteA), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Incident_SameSiteReport_StillSucceeds()
    {
        var result = await IncidentHandler(Site(SiteA))
            .Handle(IncidentFor(SiteA, deviceSite: SiteA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Incident_UnknownSite_IsA404_NotADatabaseError()
    {
        var result = await IncidentHandler(Site(SiteA))
            .Handle(IncidentFor(Ghost, deviceSite: null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Incident_ByHumanWithoutDeviceClaim_IsAllowedOnAnyExistingSite()
    {
        // Staff dùng JWT báo cháy thủ công (NS-23) không có claim iot:site_id.
        var result = await IncidentHandler(Site(SiteA), Site(SiteB))
            .Handle(IncidentFor(SiteB, deviceSite: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private sealed class NoopOutboxWriter : SharedContracts.Interfaces.IIntegrationEventOutboxWriter
    {
        public Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : SharedContracts.Events.Root.IntegrationEvent => Task.CompletedTask;
    }

    private sealed class NoopEnvironmentalMetrics : IEnvironmentalMetricsRecorder
    {
        public void IncidentDetected(string incidentType, string severity, double detectionLatencySeconds) { }
    }
}
