using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.IntegrationTests.Fixtures;

namespace NotificationService.IntegrationTests.Persistence;

/// <summary>
/// Những ràng buộc chỉ tồn tại trong Postgres: <c>CHECK</c> và chỉ mục duy nhất một phần.
/// </summary>
/// <remarks>
/// Đây là phần mà bộ unit test của service không thể chạm tới: nó dùng provider InMemory, và
/// provider đó bỏ qua toàn bộ ràng buộc ở tầng cơ sở dữ liệu. Một dòng sai hình dạng lưu êm xuôi
/// trong unit test rồi mới nổ trên môi trường thật — đúng loại lỗi mà bộ test này chặn.
/// </remarks>
[Collection(nameof(NotificationDatabaseCollection))]
public class SchemaConstraintTests : IAsyncLifetime
{
    private readonly NotificationPostgresFixture _db;

    public SchemaConstraintTests(NotificationPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- ck_notification_groups_role_filter ----------

    /// <summary>
    /// Nhóm động phải kèm bộ lọc vai trò, nhóm tĩnh thì không được có. Thiếu ràng buộc này,
    /// một nhóm động không có bộ lọc sẽ nở ra rỗng lúc gửi — không ai nhận được thông báo,
    /// và không có gì báo lỗi.
    /// </summary>
    [Fact]
    public async Task RoleGroup_WithoutRoleFilter_IsRejectedByCheckConstraint()
    {
        await using var db = _db.NewContext();
        db.NotificationGroups.Add(Group("role-no-filter", NotificationGroupKindEnum.Role, roleFilter: null));

        var act = () => db.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_notification_groups_role_filter");
    }

    [Fact]
    public async Task StaticGroup_CarryingRoleFilter_IsRejectedByCheckConstraint()
    {
        await using var db = _db.NewContext();
        db.NotificationGroups.Add(Group("static-with-filter", NotificationGroupKindEnum.Static, roleFilter: "Staff"));

        var act = () => db.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_notification_groups_role_filter");
    }

    [Theory]
    [InlineData(NotificationGroupKindEnum.Static, null)]
    [InlineData(NotificationGroupKindEnum.Role, "Manager")]
    public async Task WellShapedGroup_IsAccepted(NotificationGroupKindEnum kind, string? roleFilter)
    {
        await using var db = _db.NewContext();
        var group = Group($"ok-{kind}-{Guid.NewGuid():N}", kind, roleFilter);
        db.NotificationGroups.Add(group);

        await db.SaveChangesAsync();

        (await db.NotificationGroups.AsNoTracking().AnyAsync(g => g.Id == group.Id)).Should().BeTrue();
    }

    // ---------- ck_notification_batch_targets_shape ----------

    /// <summary>
    /// Một đích gửi nhắm nhóm thì phải có nhóm và không có người, và ngược lại. Dòng mang cả hai
    /// (hoặc không mang gì) làm bước nở người nhận không biết phải đi theo đường nào.
    /// </summary>
    [Theory]
    [InlineData(NotificationBatchTargetKindEnum.Group, false, true)]  // nhắm nhóm nhưng lại điền người
    [InlineData(NotificationBatchTargetKindEnum.Group, false, false)] // nhắm nhóm mà không có nhóm
    [InlineData(NotificationBatchTargetKindEnum.User, true, false)]   // nhắm người nhưng lại điền nhóm
    [InlineData(NotificationBatchTargetKindEnum.User, false, false)]  // nhắm người mà không có người
    public async Task MalformedBatchTarget_IsRejectedByCheckConstraint(
        NotificationBatchTargetKindEnum kind, bool withGroup, bool withUser)
    {
        await using var db = _db.NewContext();
        var batch = NewBatch();
        var group = Group($"target-{Guid.NewGuid():N}", NotificationGroupKindEnum.Static, null);
        db.NotificationBatches.Add(batch);
        db.NotificationGroups.Add(group);
        await db.SaveChangesAsync();

        db.NotificationBatchTargets.Add(new NotificationBatchTarget
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            TargetKind = kind,
            GroupId = withGroup ? group.Id : null,
            UserId = withUser ? Guid.NewGuid() : null,
        });

        var act = () => db.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ck_notification_batch_targets_shape");
    }

    [Fact]
    public async Task WellShapedBatchTargets_AreAccepted()
    {
        await using var db = _db.NewContext();
        var batch = NewBatch();
        var group = Group($"target-ok-{Guid.NewGuid():N}", NotificationGroupKindEnum.Static, null);
        db.NotificationBatches.Add(batch);
        db.NotificationGroups.Add(group);
        await db.SaveChangesAsync();

        db.NotificationBatchTargets.AddRange(
            new NotificationBatchTarget
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                TargetKind = NotificationBatchTargetKindEnum.Group,
                GroupId = group.Id,
            },
            new NotificationBatchTarget
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                TargetKind = NotificationBatchTargetKindEnum.User,
                UserId = Guid.NewGuid(),
            });

        await db.SaveChangesAsync();

        (await db.NotificationBatchTargets.AsNoTracking().CountAsync(t => t.BatchId == batch.Id))
            .Should().Be(2);
    }

    // ---------- ux_notification_groups_normalized_name (chỉ mục duy nhất một phần) ----------

    [Fact]
    public async Task TwoActiveGroups_WithTheSameNormalizedName_Collide()
    {
        var name = $"dup-{Guid.NewGuid():N}";
        await using var db = _db.NewContext();
        db.NotificationGroups.Add(Group(name, NotificationGroupKindEnum.Static, null));
        await db.SaveChangesAsync();

        db.NotificationGroups.Add(Group(name, NotificationGroupKindEnum.Static, null));

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Chỉ mục có bộ lọc <c>is_deleted = false</c> nên xoá nhóm rồi tạo lại đúng tên đó phải được.
    /// Nếu chỉ mục để toàn phần, một cái tên bị "cháy" vĩnh viễn sau lần xoá đầu tiên.
    /// </summary>
    [Fact]
    public async Task NameOfASoftDeletedGroup_CanBeReused()
    {
        var name = $"reuse-{Guid.NewGuid():N}";
        await using var db = _db.NewContext();
        var first = Group(name, NotificationGroupKindEnum.Static, null);
        db.NotificationGroups.Add(first);
        await db.SaveChangesAsync();

        db.NotificationGroups.Remove(first);   // interceptor chuyển thành xoá mềm
        await db.SaveChangesAsync();

        var second = Group(name, NotificationGroupKindEnum.Static, null);
        db.NotificationGroups.Add(second);
        await db.SaveChangesAsync();

        var rows = await db.NotificationGroups.AsNoTracking()
            .Where(g => g.NormalizedName == name.ToUpperInvariant())
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Count(g => !g.IsDeleted).Should().Be(1);
    }

    /// <summary>Mỗi vai trò chỉ được có đúng một nhóm động — seeder chạy lại không được đẻ thêm.</summary>
    [Fact]
    public async Task TwoActiveRoleGroups_ForTheSameRole_Collide()
    {
        var role = $"Role{Guid.NewGuid():N}"[..12];
        await using var db = _db.NewContext();
        db.NotificationGroups.Add(Group($"r1-{Guid.NewGuid():N}", NotificationGroupKindEnum.Role, role));
        await db.SaveChangesAsync();

        db.NotificationGroups.Add(Group($"r2-{Guid.NewGuid():N}", NotificationGroupKindEnum.Role, role));

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------- device_tokens.token duy nhất ----------

    /// <summary>
    /// Một token thiết bị chỉ được thuộc về một tài khoản. Cài lại app rồi đăng nhập bằng tài khoản
    /// khác trên cùng máy sẽ gửi lại đúng token cũ; hai dòng cùng token nghĩa là thông báo riêng tư
    /// của người này rơi vào máy người kia.
    /// </summary>
    [Fact]
    public async Task SameDeviceToken_ForTwoAccounts_IsRejected()
    {
        var token = $"ExponentPushToken[{Guid.NewGuid():N}]";
        await using var db = _db.NewContext();
        db.DeviceTokens.Add(NewToken(token));
        await db.SaveChangesAsync();

        db.DeviceTokens.Add(NewToken(token));

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------- helpers ----------

    private static NotificationGroup Group(string name, NotificationGroupKindEnum kind, string? roleFilter) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Kind = kind,
            RoleFilter = roleFilter,
        };

    private static NotificationBatch NewBatch() =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketAssigned,
            Title = "Bảo trì định kỳ",
            Body = "Lịch bảo trì sắp tới.",
            ChannelValues = new[] { (int)NotificationChannelEnum.InApp },
            Source = NotificationBatchSourceEnum.Manual,
            Status = NotificationBatchStatusEnum.Pending,
        };

    private static DeviceToken NewToken(string token) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = token,
            Platform = DevicePlatformEnum.Android,
            IsActive = true,
        };
}
