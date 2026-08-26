using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.IntegrationTests.Fixtures;

namespace NotificationService.IntegrationTests.Persistence;

/// <summary>
/// Hành vi của <c>AuditableEntityInterceptor</c> khi chạy trên Postgres thật.
/// </summary>
/// <remarks>
/// Dự án này <b>không</b> cấu hình bộ lọc truy vấn toàn cục, nên xoá mềm chỉ có tác dụng khi mọi
/// truy vấn tự thêm <c>!IsDeleted</c>. Bộ test này ghim hai nửa của giao kèo đó: interceptor thật
/// sự chuyển <c>Remove</c> thành đánh dấu, và dòng đã đánh dấu <b>vẫn</b> trả về nếu truy vấn quên
/// lọc — để không ai nhầm rằng chỉ cần gọi <c>Remove</c> là dữ liệu biến mất.
/// </remarks>
[Collection(nameof(NotificationDatabaseCollection))]
public class AuditableInterceptorTests : IAsyncLifetime
{
    private readonly NotificationPostgresFixture _db;

    public AuditableInterceptorTests(NotificationPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_StampsCreatedAt()
    {
        await using var db = _db.NewContext();
        var notification = NewNotification();

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var saved = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
        saved.CreatedAt.Should().NotBe(default);
        saved.IsDeleted.Should().BeFalse();
        saved.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Update_StampsUpdatedAt()
    {
        await using var db = _db.NewContext();
        var notification = NewNotification();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        notification.Status = NotificationStatusEnum.Sent;
        await db.SaveChangesAsync();

        var saved = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
        saved.UpdatedAt.Should().NotBeNull();
        saved.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    [Fact]
    public async Task Remove_BecomesASoftDelete_TheRowSurvives()
    {
        await using var db = _db.NewContext();
        var notification = NewNotification();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        db.Notifications.Remove(notification);
        await db.SaveChangesAsync();

        var saved = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
        saved.IsDeleted.Should().BeTrue();
        saved.DeletedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Không có bộ lọc truy vấn toàn cục: truy vấn quên <c>!IsDeleted</c> vẫn thấy dòng đã xoá.
    /// Ghim lại để mai này ai đó bật bộ lọc toàn cục thì test này đỏ và buộc phải rà lại chỗ nào
    /// đang dựa vào hành vi hiện tại, thay vì âm thầm đổi kết quả của hàng loạt truy vấn.
    /// </summary>
    [Fact]
    public async Task SoftDeletedRow_IsStillReturned_WhenTheQueryForgetsTheFilter()
    {
        await using var db = _db.NewContext();
        var notification = NewNotification();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        db.Notifications.Remove(notification);
        await db.SaveChangesAsync();

        var unfiltered = await db.Notifications.AsNoTracking().CountAsync(n => n.Id == notification.Id);
        var filtered = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.Id == notification.Id && !n.IsDeleted);

        unfiltered.Should().Be(1, "dự án không bật bộ lọc truy vấn toàn cục");
        filtered.Should().Be(0);
    }

    private static Notification NewNotification() =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketAssigned,
            Channel = NotificationChannelEnum.InApp,
            Status = NotificationStatusEnum.Pending,
            Title = "Ticket đã được giao",
            Body = "Bạn được giao một ticket mới.",
        };
}
