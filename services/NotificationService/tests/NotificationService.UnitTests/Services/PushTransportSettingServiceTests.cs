using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// ADR-0019 — đọc/ghi đường vận chuyển push.
///
/// <para>Hai tính chất quan trọng nhất được khoá ở đây: (1) hàm ĐỌC không bao giờ ném, vì nó nằm
/// trên đường đi của từng lần gửi thông báo; (2) hàm GHI có xoá cache, nếu không thì đổi cấu hình
/// trên màn hình Admin xong vẫn phải chờ hết TTL mới có tác dụng.</para>
/// </summary>
public class PushTransportSettingServiceTests
{
    private static NotificationSetting Row(string value) => new()
    {
        Id = Guid.NewGuid(),
        Key = NotificationSettingKeys.PushTransport,
        Value = value,
    };

    private static Mock<ICacheService> EmptyCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((string?)null);
        return m;
    }

    private static (PushTransportSettingService sut,
                    Mock<INotificationUnitOfWork> uow,
                    Mock<IGenericRepository<NotificationSetting>> settings,
                    Mock<ICacheService> cache)
        Build(
            IEnumerable<NotificationSetting>? seed = null,
            Mock<ICacheService>? cache = null,
            PushTransportEnum defaultTransport = PushTransportEnum.SignalR)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var data = (seed ?? Array.Empty<NotificationSetting>()).ToList();
        var settings = new Mock<IGenericRepository<NotificationSetting>>();
        settings.Setup(r => r.GetAllAsync()).Returns(() => data.AsQueryable().BuildMock());
        settings.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(() => data.AsQueryable().BuildMock());
        settings.Setup(r => r.AddAsync(It.IsAny<NotificationSetting>()))
                .Callback<NotificationSetting>(data.Add)
                .Returns(Task.CompletedTask);
        uow.SetupGet(u => u.NotificationSettings).Returns(settings.Object);

        var cacheMock = cache ?? EmptyCache();

        var sut = new PushTransportSettingService(
            uow.Object,
            cacheMock.Object,
            Options.Create(new NotificationPushOptions { DefaultTransport = defaultTransport }),
            NullLogger<PushTransportSettingService>.Instance);

        return (sut, uow, settings, cacheMock);
    }

    // ════════════════════════ Đọc ════════════════════════

    [Fact]
    public async Task BangTrong_TraVeMacDinhTuCauHinh()
    {
        var (sut, _, _, _) = Build(defaultTransport: PushTransportEnum.Both);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.Both);
    }

    [Theory]
    [InlineData("SignalR", PushTransportEnum.SignalR)]
    [InlineData("Expo", PushTransportEnum.Expo)]
    [InlineData("Both", PushTransportEnum.Both)]
    [InlineData("both", PushTransportEnum.Both)]      // không phân biệt hoa thường
    [InlineData("3", PushTransportEnum.Both)]         // giá trị nhập tay dạng số
    public async Task DocDuocCaTenLanSo(string stored, PushTransportEnum expected)
    {
        var (sut, _, _, _) = Build([Row(stored)]);

        (await sut.GetAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("khong-phai-transport")]
    [InlineData("99")]
    [InlineData("")]
    public async Task GiaTriRac_RoiVeMacDinhChuKhongNem(string stored)
    {
        var (sut, _, _, _) = Build([Row(stored)], defaultTransport: PushTransportEnum.SignalR);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.SignalR);
    }

    [Fact]
    public async Task BoQuaDongDaXoaMem()
    {
        var deleted = Row("Expo");
        deleted.IsDeleted = true;
        var (sut, _, _, _) = Build([deleted], defaultTransport: PushTransportEnum.SignalR);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.SignalR);
    }

    [Fact]
    public async Task CacheLoi_VanDocDuocTuDatabase()
    {
        // Redis chết không được phép làm đứng cả kênh Push.
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("redis down"));
        cache.Setup(c => c.SetAsync(
                 It.IsAny<string>(), It.IsAny<string>(),
                 It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("redis down"));

        var (sut, _, _, _) = Build([Row("Expo")], cache);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.Expo);
    }

    [Fact]
    public async Task DatabaseLoi_RoiVeMacDinhChuKhongNem()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var settings = new Mock<IGenericRepository<NotificationSetting>>();
        settings.Setup(r => r.GetAllAsync()).Throws(new InvalidOperationException("db down"));
        uow.SetupGet(u => u.NotificationSettings).Returns(settings.Object);

        var sut = new PushTransportSettingService(
            uow.Object,
            EmptyCache().Object,
            Options.Create(new NotificationPushOptions { DefaultTransport = PushTransportEnum.Both }),
            NullLogger<PushTransportSettingService>.Instance);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.Both);
    }

    [Fact]
    public async Task CoCache_KhongDungToiDatabase()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("Expo");

        var (sut, _, settings, _) = Build([Row("SignalR")], cache);

        (await sut.GetAsync()).Should().Be(PushTransportEnum.Expo);
        settings.Verify(r => r.GetAllAsync(), Times.Never);
    }

    // ════════════════════════ Ghi ════════════════════════

    [Fact]
    public async Task ChuaCoDong_ThiThemMoi()
    {
        var (sut, uow, settings, _) = Build();

        await sut.SetAsync(PushTransportEnum.Expo);

        settings.Verify(r => r.AddAsync(It.Is<NotificationSetting>(
            s => s.Key == NotificationSettingKeys.PushTransport && s.Value == "Expo")), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DaCoDong_ThiCapNhatChuKhongThemDongThuHai()
    {
        var row = Row("SignalR");
        var (sut, _, settings, _) = Build([row]);

        await sut.SetAsync(PushTransportEnum.Both);

        row.Value.Should().Be("Both");
        settings.Verify(r => r.AddAsync(It.IsAny<NotificationSetting>()), Times.Never);
        settings.Verify(r => r.UpdateAsync(row), Times.Once);
    }

    [Fact]
    public async Task GhiXongThiXoaCache_DeCoHieuLucNgay()
    {
        var (sut, _, _, cache) = Build();

        await sut.SetAsync(PushTransportEnum.Expo);

        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task XoaCacheLoi_KhongLamHongLanGhiDaThanhCong()
    {
        var cache = EmptyCache();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("redis down"));

        var (sut, uow, _, _) = Build(cache: cache);

        var act = async () => await sut.SetAsync(PushTransportEnum.Expo);

        await act.Should().NotThrowAsync();
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GiaTriNgoaiDai_ThiNem()
    {
        // Ghi thì phải ném: người vận hành cần biết là chưa đổi được, khác hẳn với đường đọc.
        var (sut, _, _, _) = Build();

        var act = async () => await sut.SetAsync((PushTransportEnum)99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
