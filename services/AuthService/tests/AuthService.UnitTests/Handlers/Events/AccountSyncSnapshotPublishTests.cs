using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Events;

/// <summary>
/// 02/08/2026 — Khoá lại hợp đồng: mọi thao tác làm đổi <c>Role</c> hoặc làm đổi "còn nhận thông
/// báo hay không" của một account đều PHẢI phát <see cref="AccountSyncSnapshotEvent"/>.
///
/// Đây là các lỗi có thật đã đo được: read-model account bên NotificationService chỉ có 2/10 dòng,
/// và không có đường nào cập nhật được role hay trạng thái sau khi dòng đã tồn tại. Hệ quả là
/// <c>GetActiveByRoleAsync("Admin")</c> trả rỗng nên mọi thông báo gửi cho nhóm Admin bị bỏ qua
/// im lặng, còn đổi role thì gửi sai người.
///
/// Cặp chuyển trạng thái Active ↔ Locked KHÔNG có test ở đây là có chủ ý — xem
/// <c>AccountStatusEnumExtensions.IsNotifiable</c>: khoá tạm do sai mật khẩu không làm đổi giá trị
/// đó, nên <c>LoginCommandHandler</c> và <c>UnlockAccountCommandHandler</c> không cần phát gì.
/// </summary>
public class AccountSyncSnapshotPublishTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    private List<AccountSyncSnapshotEvent> Captured()
    {
        var captured = new List<AccountSyncSnapshotEvent>();
        _producer
            .Setup(p => p.PublishAsync(It.IsAny<AccountSyncSnapshotEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountSyncSnapshotEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);
        return captured;
    }

    private static global::AuthService.Domain.Entities.Role NewRole(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Status = RoleStatusEnum.Active
    };

    private static global::AuthService.Domain.Entities.Account NewAccount(
        global::AuthService.Domain.Entities.Role? role = null,
        AccountStatusEnum status = AccountStatusEnum.Active,
        bool isDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "Người Dùng",
            PhoneNumber = "0901234567",
            Status = status,
            RoleId = role?.Id,
            Role = role!,
            IsDeleted = isDeleted
        };

    // ── Đổi role ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_AlsoPublishesAccountRoleChangedEvent_WithBothSides()
    {
        // GH-769 — Battery và Ticket KHÔNG nghe AccountSyncSnapshotEvent (chỉ NotificationService
        // nghe), nên trước đây đổi role xong bản sao ở hai service đó giữ nguyên role cũ: thiếu
        // StaffAccount thì không giao ticket được, thừa CustomerAccount thì vẫn giữ quyền cũ.
        var oldRole = NewRole("Customer");
        var newRole = NewRole("Staff");
        var account = NewAccount(oldRole);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { newRole });

        var captured = new List<AccountRoleChangedEvent>();
        _producer
            .Setup(p => p.PublishAsync(It.IsAny<AccountRoleChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountRoleChangedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        var resp = await handler.Handle(
            new ChangeAccountRoleCommand { AccountId = account.Id, RoleId = newRole.Id },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = captured.Should().ContainSingle().Subject;
        evt.AccountId.Should().Be(account.Id);
        // Cả HAI vế: chỉ có role mới thì consumer không suy ra được bản sao NÀO phải dọn.
        evt.OldRole.Should().Be("Customer");
        evt.NewRole.Should().Be("Staff");
        evt.Email.Should().Be(account.Email);
        evt.FullName.Should().Be(account.FullName);
    }

    [Fact]
    public async Task ChangeRole_NoOp_PublishesNoRoleChangedEvent()
    {
        // Gán lại đúng role đang có ⇒ handler trả sớm. Phát event ở đây chỉ làm consumer chạy không công.
        var role = NewRole("Customer");
        var account = NewAccount(role);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { role });

        var captured = new List<AccountRoleChangedEvent>();
        _producer
            .Setup(p => p.PublishAsync(It.IsAny<AccountRoleChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountRoleChangedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        await handler.Handle(
            new ChangeAccountRoleCommand { AccountId = account.Id, RoleId = role.Id },
            CancellationToken.None);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRole_PhatSnapshot_KemRoleMoi()
    {
        var oldRole = NewRole("Staff");
        var newRole = NewRole("Manager");
        var account = NewAccount(oldRole);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { newRole });
        var captured = Captured();

        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        var resp = await handler.Handle(
            new ChangeAccountRoleCommand { AccountId = account.Id, RoleId = newRole.Id },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].AccountId.Should().Be(account.Id);
        captured[0].Role.Should().Be("Manager", "snapshot phải chở role MỚI");
        captured[0].IsActive.Should().BeTrue();
        captured[0].IsDeleted.Should().BeFalse();
        captured[0].Reason.Should().Be("RoleChanged");
    }

    [Fact]
    public async Task ChangeRole_TrungRoleCu_ThiKhongPhatGi()
    {
        var role = NewRole("Staff");
        var account = NewAccount(role);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { role });
        var captured = Captured();

        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        await handler.Handle(
            new ChangeAccountRoleCommand { AccountId = account.Id, RoleId = role.Id },
            CancellationToken.None);

        captured.Should().BeEmpty("không có thay đổi thì không có gì để đồng bộ");
    }

    // ── Đổi trạng thái ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AccountStatusEnum.Inactive, false)]
    [InlineData(AccountStatusEnum.Suspended, false)]
    [InlineData(AccountStatusEnum.Banned, false)]
    [InlineData(AccountStatusEnum.Locked, true)]   // khoá tạm — vẫn nhận thông báo
    public async Task ChangeStatus_PhatSnapshot_DungCoIsActive(AccountStatusEnum target, bool expectedActive)
    {
        var role = NewRole("Manager");
        var account = NewAccount(role);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var captured = Captured();

        var handler = new ChangeAccountStatusCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        var resp = await handler.Handle(
            new ChangeAccountStatusCommand { Id = account.Id, Status = target },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].IsActive.Should().Be(expectedActive);
        captured[0].Role.Should().Be("Manager");
        captured[0].Reason.Should().Be("StatusChanged");
    }

    [Fact]
    public async Task ChangeStatus_TrungTrangThaiCu_ThiKhongPhatGi()
    {
        var account = NewAccount(NewRole("Manager"));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var captured = Captured();

        var handler = new ChangeAccountStatusCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        await handler.Handle(
            new ChangeAccountStatusCommand { Id = account.Id, Status = AccountStatusEnum.Active },
            CancellationToken.None);

        captured.Should().BeEmpty();
    }

    // ── Tự vô hiệu hoá ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateMe_PhatSnapshot_IsActiveFalse()
    {
        var account = NewAccount(NewRole("Customer"));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var captured = Captured();

        var handler = new DeactivateMeCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        var resp = await handler.Handle(new DeactivateMeCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].IsActive.Should().BeFalse();
        captured[0].Role.Should().Be("Customer");
    }

    // ── Đối soát ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resync_PhatSnapshotChoTungAccount_KeCaDaXoaMem()
    {
        var admin = NewAccount(NewRole("Admin"));
        var pending = NewAccount(NewRole("Customer"), AccountStatusEnum.PendingVerification);
        var removed = NewAccount(NewRole("Staff"), isDeleted: true);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { admin, pending, removed });
        var captured = Captured();

        var handler = new AccountResyncCommandHandler(uow.Object, _producer.Object);
        var resp = await handler.Handle(new AccountResyncCommand(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().HaveCount(3, "account đã xoá mềm cũng phải phát, nếu không read-model không biết mà xoá theo");

        resp.Data!.TotalAccounts.Should().Be(3);
        resp.Data.ActiveAccounts.Should().Be(1);
        resp.Data.InactiveAccounts.Should().Be(1);
        resp.Data.DeletedAccounts.Should().Be(1);

        captured.Single(e => e.AccountId == admin.Id).IsActive.Should().BeTrue();
        captured.Single(e => e.AccountId == pending.Id).IsActive.Should().BeFalse("chưa xác thực thì chưa nhận thông báo");

        var removedSnapshot = captured.Single(e => e.AccountId == removed.Id);
        removedSnapshot.IsDeleted.Should().BeTrue();
        removedSnapshot.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Resync_MotAccount_ChiPhatDungAccountDo()
    {
        var a = NewAccount(NewRole("Admin"));
        var b = NewAccount(NewRole("Manager"));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { a, b });
        var captured = Captured();

        var handler = new AccountResyncCommandHandler(uow.Object, _producer.Object);
        var resp = await handler.Handle(new AccountResyncCommand { AccountId = b.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].AccountId.Should().Be(b.Id);
    }

    [Fact]
    public async Task Resync_AccountKhongTonTai_Tra404_VaKhongPhatGi()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var captured = Captured();

        var handler = new AccountResyncCommandHandler(uow.Object, _producer.Object);
        var resp = await handler.Handle(new AccountResyncCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Resync_MoiSnapshotTrongMotLuot_DungChungMocThoiGian()
    {
        // Consumer loại snapshot có mốc <= mốc đã áp. Nếu mỗi account một mốc khác nhau thì các
        // snapshot của cùng một lượt đối soát vẫn ổn, nhưng dùng chung một mốc khiến cả lượt trở
        // thành một đơn vị so sánh duy nhất — dễ suy luận hơn khi hai lượt chạy sát nhau.
        var accounts = Enumerable.Range(0, 5).Select(_ => NewAccount(NewRole("Staff"))).ToArray();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: accounts);
        var captured = Captured();

        var handler = new AccountResyncCommandHandler(uow.Object, _producer.Object);
        await handler.Handle(new AccountResyncCommand(), CancellationToken.None);

        captured.Should().HaveCount(5);
        captured.Select(e => e.SnapshotAtUtc).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Resync_StaffSnapshot_IncludesAllAuthoritativeAssignmentFields()
    {
        var role = NewRole("Staff");
        var account = NewAccount(role);
        account.StaffProfile = new AuthService.Domain.Entities.StaffProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EmployeeCode = "EMP-007",
            MaxConcurrentTickets = 6,
            IsAvailable = false,
            SkillTier = StaffSkillTierEnum.ModuleSpecialist,
            Skills = new List<AuthService.Domain.Entities.StaffSkill>
            {
                new() { Id = Guid.NewGuid(), SkillCode = "inverter", SkillLevel = 2 },
                new() { Id = Guid.NewGuid(), SkillCode = "battery", SkillLevel = 3 },
                new() { Id = Guid.NewGuid(), SkillCode = "battery", SkillLevel = 1 },
                new() { Id = Guid.NewGuid(), SkillCode = "retired", SkillLevel = 1, IsDeleted = true }
            }
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var captured = Captured();

        await new AccountResyncCommandHandler(uow.Object, _producer.Object)
            .Handle(new AccountResyncCommand(), CancellationToken.None);

        var snapshot = captured.Should().ContainSingle().Subject;
        snapshot.AccountStatus.Should().Be((int)AccountStatusEnum.Active);
        snapshot.HasStaffProfileSnapshot.Should().BeTrue();
        snapshot.EmployeeCode.Should().Be("EMP-007");
        snapshot.MaxConcurrentTickets.Should().Be(6);
        snapshot.IsAvailable.Should().BeFalse();
        snapshot.SkillTier.Should().Be((int)StaffSkillTierEnum.ModuleSpecialist);
        snapshot.SkillCodes.Should().Equal("battery", "inverter");
    }

    // ── Khôi phục tài khoản đã xoá ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateVerify_PhatSnapshot_BoCoXoaMem()
    {
        var role = NewRole("Customer");
        var account = NewAccount(role, AccountStatusEnum.Inactive, isDeleted: true);
        account.DeletedAt = DateTime.UtcNow.AddDays(-5);
        account.OtpCode = "123456";
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(5);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var captured = Captured();

        var handler = new ReactivateVerifyCommandHandler(uow.Object, MockPublisher.NoOp().Object, _producer.Object);
        var resp = await handler.Handle(
            new ReactivateVerifyCommand { Email = account.Email, Otp = "123456" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle();
        captured[0].IsDeleted.Should().BeFalse("read-model phải bỏ cờ xoá mềm, nếu không tài khoản khôi phục sẽ không bao giờ nhận được thông báo nữa");
        captured[0].IsActive.Should().BeTrue();
        captured[0].Role.Should().Be("Customer");
        captured[0].Reason.Should().Be("Reactivated");
    }
}
