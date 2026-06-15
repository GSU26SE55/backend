using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 5B #91/#92 — Ambient ingest + history + threshold handlers.
/// </summary>
public class AmbientHandlersTests
{
    private static Mock<IBatteryUnitOfWork> BuildUow(
        List<AmbientReading>? readings = null,
        List<AmbientThresholdConfig>? thresholds = null)
    {
        readings ??= new List<AmbientReading>();
        thresholds ??= new List<AmbientThresholdConfig>();
        var uow = new Mock<IBatteryUnitOfWork>();
        var readingsRepo = new Mock<IGenericRepository<AmbientReading>>();
        var thresholdsRepo = new Mock<IGenericRepository<AmbientThresholdConfig>>();
        readingsRepo.Setup(r => r.GetAllAsync()).Returns(readings.AsQueryable().BuildMock());
        thresholdsRepo.Setup(r => r.GetAllAsync()).Returns(thresholds.AsQueryable().BuildMock());
        uow.SetupGet(u => u.AmbientReadings).Returns(readingsRepo.Object);
        uow.SetupGet(u => u.AmbientThresholdConfigs).Returns(thresholdsRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }

    // ===== Batch ingest =====

    [Fact]
    public async Task BatchIngest_ValidItems_ShouldSave()
    {
        var uow = BuildUow();
        var handler = new BatchIngestAmbientReadingsCommandHandler(uow.Object);

        var siteId = Guid.NewGuid();
        var result = await handler.Handle(new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = siteId, Time = DateTime.UtcNow, AmbientTemperature = 30m, Humidity = 65m },
                new() { SiteId = siteId, Time = DateTime.UtcNow.AddMinutes(-1), AmbientTemperature = 29m, Humidity = 64m }
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(2);
        uow.Verify(u => u.AmbientReadings.AddAsync(It.IsAny<AmbientReading>()), Times.Exactly(2));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchIngest_Validation_TooManyItems_ShouldFail()
    {
        var cmd = new BatchIngestAmbientReadingsCommand
        {
            Items = Enumerable.Range(0, 101)
                .Select(_ => new AmbientReadingItem
                {
                    SiteId = Guid.NewGuid(),
                    Time = DateTime.UtcNow,
                    AmbientTemperature = 25m,
                    Humidity = 50m
                }).ToList()
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BatchIngest_Validation_HumidityOutOfRange_ShouldFail()
    {
        var cmd = new BatchIngestAmbientReadingsCommand
        {
            Items = new List<AmbientReadingItem>
            {
                new() { SiteId = Guid.NewGuid(), Time = DateTime.UtcNow,
                        AmbientTemperature = 25m, Humidity = 105m }
            }
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Detail.Contains("Humidity"));
    }

    // ===== History query =====

    [Fact]
    public async Task GetHistory_ShouldReturnPaginatedSortedDesc()
    {
        var siteId = Guid.NewGuid();
        var older = new AmbientReading { Time = DateTime.UtcNow.AddHours(-1), SiteId = siteId, AmbientTemperature = 28m, Humidity = 60m, Source = AmbientReadingSourceEnum.IotSensor };
        var newer = new AmbientReading { Time = DateTime.UtcNow, SiteId = siteId, AmbientTemperature = 30m, Humidity = 70m, Source = AmbientReadingSourceEnum.IotSensor };
        var uow = BuildUow(readings: new List<AmbientReading> { older, newer });

        var handler = new GetAmbientReadingHistoryQueryHandler(uow.Object);
        var result = await handler.Handle(new GetAmbientReadingHistoryQuery { SiteId = siteId, PageSize = 10 }, default);

        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items[0].AmbientTemperature.Should().Be(30m); // newest first
    }

    [Fact]
    public async Task GetLatest_NoData_ShouldReturn404()
    {
        var uow = BuildUow();
        var handler = new GetLatestAmbientReadingQueryHandler(uow.Object);

        var result = await handler.Handle(new GetLatestAmbientReadingQuery { SiteId = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetLatest_WithData_ShouldReturnMostRecent()
    {
        var siteId = Guid.NewGuid();
        var older = new AmbientReading { Time = DateTime.UtcNow.AddHours(-1), SiteId = siteId, AmbientTemperature = 28m, Humidity = 60m, Source = AmbientReadingSourceEnum.IotSensor };
        var newer = new AmbientReading { Time = DateTime.UtcNow, SiteId = siteId, AmbientTemperature = 33m, Humidity = 70m, Source = AmbientReadingSourceEnum.IotSensor };
        var uow = BuildUow(readings: new List<AmbientReading> { older, newer });

        var handler = new GetLatestAmbientReadingQueryHandler(uow.Object);
        var result = await handler.Handle(new GetLatestAmbientReadingQuery { SiteId = siteId }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.AmbientTemperature.Should().Be(33m);
    }

    // ===== Threshold upsert =====

    [Fact]
    public async Task Upsert_CreateNew_WhenNotExist()
    {
        var siteId = Guid.NewGuid();
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig>());

        var handler = new UpsertAmbientThresholdConfigCommandHandler(uow.Object);
        var result = await handler.Handle(new UpsertAmbientThresholdConfigCommand
        {
            SiteId = siteId,
            HighAmbientTempWarning = 35m,
            HighAmbientTempCritical = 40m,
            Enabled = true
        }, default);

        result.IsSuccess.Should().BeTrue();
        uow.Verify(u => u.AmbientThresholdConfigs.AddAsync(It.IsAny<AmbientThresholdConfig>()), Times.Once);
        uow.Verify(u => u.AmbientThresholdConfigs.UpdateAsync(It.IsAny<AmbientThresholdConfig>()), Times.Never);
    }

    [Fact]
    public async Task Upsert_UpdateExisting_WhenAlreadyExists()
    {
        var siteId = Guid.NewGuid();
        var existing = new AmbientThresholdConfig
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            HighAmbientTempWarning = 30m,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig> { existing });

        var handler = new UpsertAmbientThresholdConfigCommandHandler(uow.Object);
        var result = await handler.Handle(new UpsertAmbientThresholdConfigCommand
        {
            SiteId = siteId,
            HighAmbientTempWarning = 38m,
            HighAmbientTempCritical = 42m
        }, default);

        result.IsSuccess.Should().BeTrue();
        existing.HighAmbientTempWarning.Should().Be(38m);
        uow.Verify(u => u.AmbientThresholdConfigs.AddAsync(It.IsAny<AmbientThresholdConfig>()), Times.Never);
        uow.Verify(u => u.AmbientThresholdConfigs.UpdateAsync(It.IsAny<AmbientThresholdConfig>()), Times.Once);
    }

    [Fact]
    public async Task Upsert_Validation_CriticalLowerThanWarning_ShouldFail()
    {
        var cmd = new UpsertAmbientThresholdConfigCommand
        {
            SiteId = Guid.NewGuid(),
            HighAmbientTempWarning = 40m,
            HighAmbientTempCritical = 35m
        };

        var result = await cmd.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetThresholdBySite_NotFound_ShouldReturn404()
    {
        var uow = BuildUow(thresholds: new List<AmbientThresholdConfig>());
        var handler = new GetAmbientThresholdBySiteQueryHandler(uow.Object);

        var result = await handler.Handle(new GetAmbientThresholdBySiteQuery { SiteId = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ListThresholds_ShouldReturnPaginated()
    {
        var thresholds = new List<AmbientThresholdConfig>
        {
            new() { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Enabled = true, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), Enabled = true, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        };
        var uow = BuildUow(thresholds: thresholds);

        var handler = new ListAmbientThresholdsQueryHandler(uow.Object);
        var result = await handler.Handle(new ListAmbientThresholdsQuery { PageSize = 10 }, default);

        result.Data!.TotalItems.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
    }
}
