using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Handler.Permission;
using AuthService.Domain.Entities;
using AuthService.UnitTests.Helpers;
using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.PermissionTests;

/// <summary>
/// GH-771 — đổi permission của role không xoá cache phân quyền.
///
/// <para>
/// AuthService có sẵn <c>PermissionsChangedConsumer</c> để gọi
/// <c>PermissionResolver.Invalidate…</c>, nhưng KHÔNG NƠI NÀO phát
/// <c>PermissionsChangedEvent</c>. Cache role-permission sống 5 phút, nên một quyền vừa bị THU HỒI
/// vẫn còn hiệu lực tới hết TTL — năm phút hệ thống cố tình cho qua thứ mà quản trị viên vừa chặn.
/// </para>
/// </summary>
public class PermissionsChangedEventTests
{
    private sealed class CapturingProducer : IMessageProducerService
    {
        public List<PermissionsChangedEvent> Events { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : SharedContracts.Events.Root.IntegrationEvent
        {
            if (message is PermissionsChangedEvent e)
                Events.Add(e);
            return Task.CompletedTask;
        }
    }

    private static Permission NewPerm(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Module = code.Split('.')[0],
        Description = code,
        IsSystemPermission = false,
        CreatedAt = DateTime.UtcNow,
    };

    private static Role NewRole() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Manager",
        NormalizedName = "MANAGER",
        Status = Domain.Enums.RoleStatusEnum.Active,
    };

    [Fact]
    public async Task GrantingPermissions_PublishesBoundToRole_WithCodes()
    {
        var role = NewRole();
        var p1 = NewPerm("ticket.assign");
        var p2 = NewPerm("ticket.close");
        var (uow, _, _, _) = MockUnitOfWork.Build(
            roleSeed: new[] { role }, permissionSeed: new[] { p1, p2 });
        var producer = new CapturingProducer();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(
            new SetRolePermissionsCommand { RoleId = role.Id, PermissionIds = new List<Guid> { p1.Id, p2.Id } },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.Events.Should().ContainSingle().Subject;
        evt.ChangeKind.Should().Be("BoundToRole");
        // Consumer tra role theo Role.NormalizedName — sai cột này là xoá cache trượt, im lặng.
        evt.RoleCode.Should().Be("MANAGER");
        evt.AffectedPermissionCodes.Should().BeEquivalentTo("ticket.assign", "ticket.close");
    }

    [Fact]
    public async Task RevokingPermissions_PublishesUnboundFromRole()
    {
        // Đây là ca QUAN TRỌNG NHẤT của issue: thu hồi quyền mà cache không đổi nghĩa là quyền vẫn
        // dùng được tiếp 5 phút.
        var role = NewRole();
        var p1 = NewPerm("ticket.assign");
        var p2 = NewPerm("ticket.close");
        var (uow, _, _, _) = MockUnitOfWork.Build(
            roleSeed: new[] { role },
            permissionSeed: new[] { p1, p2 },
            rolePermissionSeed: new[]
            {
                new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p1.Id },
                new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p2.Id },
            });
        var producer = new CapturingProducer();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(
            new SetRolePermissionsCommand { RoleId = role.Id, PermissionIds = new List<Guid> { p1.Id } },
            CancellationToken.None);

        var evt = producer.Events.Should().ContainSingle().Subject;
        evt.ChangeKind.Should().Be("UnboundFromRole");
        evt.AffectedPermissionCodes.Should().BeEquivalentTo("ticket.close");
    }

    [Fact]
    public async Task MixedGrantAndRevoke_PublishesBothKinds_EachWithItsOwnCodes()
    {
        // Thêm và gỡ là hai sự việc khác nhau. Gộp làm một sẽ phải chọn bừa một ChangeKind và làm
        // sai lệch nhật ký; xoá cache là thao tác lũy đẳng nên hai event là vô hại.
        var role = NewRole();
        var keep = NewPerm("ticket.view");
        var removed = NewPerm("ticket.close");
        var added = NewPerm("ticket.assign");
        var (uow, _, _, _) = MockUnitOfWork.Build(
            roleSeed: new[] { role },
            permissionSeed: new[] { keep, removed, added },
            rolePermissionSeed: new[]
            {
                new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = keep.Id },
                new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = removed.Id },
            });
        var producer = new CapturingProducer();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(
            new SetRolePermissionsCommand { RoleId = role.Id, PermissionIds = new List<Guid> { keep.Id, added.Id } },
            CancellationToken.None);

        producer.Events.Should().HaveCount(2);
        producer.Events.Should().ContainSingle(e => e.ChangeKind == "UnboundFromRole")
            .Which.AffectedPermissionCodes.Should().BeEquivalentTo("ticket.close");
        producer.Events.Should().ContainSingle(e => e.ChangeKind == "BoundToRole")
            .Which.AffectedPermissionCodes.Should().BeEquivalentTo("ticket.assign");
    }

    [Fact]
    public async Task NoActualChange_PublishesNothing()
    {
        // Gửi lại đúng tập đang có ⇒ không có gì đổi. Xoá cache ở đây chỉ làm mọi role phải đọc lại
        // DB không vì lý do gì.
        var role = NewRole();
        var p1 = NewPerm("ticket.view");
        var (uow, _, _, _) = MockUnitOfWork.Build(
            roleSeed: new[] { role },
            permissionSeed: new[] { p1 },
            rolePermissionSeed: new[]
            {
                new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p1.Id },
            });
        var producer = new CapturingProducer();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        await handler.Handle(
            new SetRolePermissionsCommand { RoleId = role.Id, PermissionIds = new List<Guid> { p1.Id } },
            CancellationToken.None);

        producer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedValidation_PublishesNothing()
    {
        // Role không tồn tại ⇒ handler trả 404 và không sửa gì. Phát event ở đây là xoá cache cho
        // một thay đổi chưa từng xảy ra.
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var producer = new CapturingProducer();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, MockPublisher.NoOp().Object, producer);

        var resp = await handler.Handle(
            new SetRolePermissionsCommand { RoleId = Guid.NewGuid(), PermissionIds = new List<Guid>() },
            CancellationToken.None);

        resp.StatusCode.Should().Be(404);
        producer.Events.Should().BeEmpty();
    }
}
