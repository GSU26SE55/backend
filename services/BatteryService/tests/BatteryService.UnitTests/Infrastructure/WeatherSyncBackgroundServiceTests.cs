using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// Sprint 5B #90 — WeatherSync dedup + interval logic.
/// Test internal RunOnceAsync gọn không cần BackgroundService lifecycle.
/// </summary>
public class WeatherSyncBackgroundServiceTests
{
    private static (Mock<IBatteryUnitOfWork> Uow, Mock<IOpenMeteoClient> Client,
                    Mock<IGenericRepository<Site>> SitesRepo,
                    Mock<IGenericRepository<AmbientReading>> ReadingsRepo)
        BuildMocks(List<Site> sites, List<AmbientReading> recentReadings)
    {
        var uow = new Mock<IBatteryUnitOfWork>();
        var sitesRepo = new Mock<IGenericRepository<Site>>();
        var readingsRepo = new Mock<IGenericRepository<AmbientReading>>();

        sitesRepo.Setup(r => r.GetAllAsync()).Returns(sites.AsQueryable().BuildMock());
        readingsRepo.Setup(r => r.GetAllAsync()).Returns(recentReadings.AsQueryable().BuildMock());

        uow.SetupGet(u => u.Sites).Returns(sitesRepo.Object);
        uow.SetupGet(u => u.AmbientReadings).Returns(readingsRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var client = new Mock<IOpenMeteoClient>();
        return (uow, client, sitesRepo, readingsRepo);
    }

    private static WeatherSyncBackgroundService Build(IServiceProvider provider, WeatherSyncOptions opt)
    {
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new WeatherSyncBackgroundService(scopeFactory,
            Options.Create(opt), NullLogger<WeatherSyncBackgroundService>.Instance);
    }

    private static IServiceProvider BuildProvider(IBatteryUnitOfWork uow, IOpenMeteoClient client)
    {
        return new ServiceCollection()
            .AddSingleton(uow)
            .AddSingleton(client)
            .BuildServiceProvider();
    }

    [Fact]
    public async Task RunOnce_NoSites_ShouldDoNothing()
    {
        var (uow, client, _, _) = BuildMocks(new List<Site>(), new List<AmbientReading>());
        var provider = BuildProvider(uow.Object, client.Object);
        var sut = Build(provider, new WeatherSyncOptions { Enabled = true });

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await sut.StartAsync(cts.Token);
        await Task.Delay(50);
        await sut.StopAsync(default);

        client.Verify(c => c.GetCurrentAsync(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunOnce_SiteWithRecentReading_ShouldDedupAndSkip()
    {
        var siteId = Guid.NewGuid();
        var site = new Site
        {
            Id = siteId,
            Name = "S",
            CustomerId = Guid.NewGuid(),
            Latitude = 10m,
            Longitude = 106m,
            InstallDate = DateTime.UtcNow,
            Status = SiteStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        };
        var recent = new AmbientReading
        {
            Time = DateTime.UtcNow.AddMinutes(-5),
            SiteId = siteId,
            AmbientTemperature = 30m,
            Humidity = 60m,
            Source = AmbientReadingSourceEnum.WeatherApi
        };
        var (uow, client, _, _) = BuildMocks(new List<Site> { site }, new List<AmbientReading> { recent });
        client.Setup(c => c.GetCurrentAsync(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AmbientWeatherSample(30m, 60m, DateTime.UtcNow));

        var provider = BuildProvider(uow.Object, client.Object);
        var sut = Build(provider, new WeatherSyncOptions { Enabled = true, DedupWindowMinutes = 10 });

        var cts = new CancellationTokenSource();
        cts.CancelAfter(150);
        await sut.StartAsync(cts.Token);
        await Task.Delay(80);
        await sut.StopAsync(default);

        client.Verify(c => c.GetCurrentAsync(It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnce_FreshSite_ShouldFetchAndInsert()
    {
        var siteId = Guid.NewGuid();
        var site = new Site
        {
            Id = siteId,
            Name = "S",
            CustomerId = Guid.NewGuid(),
            Latitude = 10m,
            Longitude = 106m,
            InstallDate = DateTime.UtcNow,
            Status = SiteStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        };
        var (uow, client, _, readingsRepo) = BuildMocks(new List<Site> { site }, new List<AmbientReading>());

        client.Setup(c => c.GetCurrentAsync(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AmbientWeatherSample(32m, 70m, DateTime.UtcNow));

        var provider = BuildProvider(uow.Object, client.Object);
        var sut = Build(provider, new WeatherSyncOptions { Enabled = true });

        var cts = new CancellationTokenSource();
        cts.CancelAfter(150);
        await sut.StartAsync(cts.Token);
        await Task.Delay(80);
        await sut.StopAsync(default);

        readingsRepo.Verify(r => r.AddAsync(It.IsAny<AmbientReading>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunOnce_Disabled_ShouldNotPollAtAll()
    {
        var (uow, client, _, _) = BuildMocks(new List<Site>(), new List<AmbientReading>());
        var provider = BuildProvider(uow.Object, client.Object);
        var sut = Build(provider, new WeatherSyncOptions { Enabled = false });

        var cts = new CancellationTokenSource();
        cts.CancelAfter(150);
        await sut.StartAsync(cts.Token);
        await Task.Delay(80);
        await sut.StopAsync(default);

        uow.Verify(u => u.Sites.GetAllAsync(), Times.Never);
    }
}
