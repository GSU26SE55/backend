using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

// #AUTH-62: GDPR Article 20 data portability — verify export endpoint trả đúng + LOẠI BỎ
// security material (PasswordHash, TwoFactorSecret, refresh TokenHash, BackupCode CodeHash).
public class ExportMyDataQueryHandlerTests
{
    private static Account SeedAccount(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "u@example.com",
        PasswordHash = "DO_NOT_LEAK_HASH",
        FullName = "U",
        PhoneNumber = "0900111222",
        Status = AccountStatusEnum.Active,
        EmailConfirmed = true,
        PhoneConfirmed = true,
        TwoFactorEnabled = true,
        TwoFactorSecret = "DO_NOT_LEAK_SECRET",
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Handle_AccountNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new ExportMyDataQueryHandler(uow.Object);

        var resp = await handler.Handle(new ExportMyDataQuery { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_AccountOnly_ReturnsSnapshot_WithoutSecurityMaterial()
    {
        var account = SeedAccount();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ExportMyDataQueryHandler(uow.Object);

        var resp = await handler.Handle(new ExportMyDataQuery { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data.Should().NotBeNull();
        resp.Data!.Account.Email.Should().Be("u@example.com");
        resp.Data.Account.Id.Should().Be(account.Id);
        resp.Data.Format.Should().Be("json");
        resp.Data.Version.Should().Be("1.0");
        resp.Data.ExportedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // #AUTH-62 GDPR — security material PHẢI KHÔNG bị export ra ngoài.
        var json = System.Text.Json.JsonSerializer.Serialize(resp.Data);
        json.Should().NotContain("DO_NOT_LEAK_HASH");
        json.Should().NotContain("DO_NOT_LEAK_SECRET");
        json.Should().NotContain("PasswordHash");
        json.Should().NotContain("TwoFactorSecret");
    }

    [Fact]
    public async Task Handle_WithSessionsAndAudit_IncludesAllInSnapshot()
    {
        var account = SeedAccount();
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "HASH_DO_NOT_LEAK",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7),
            IpAddress = "10.0.0.1",
            UserAgent = "Test/1.0"
        };
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TargetAccountId = account.Id,
            ActorAccountId = account.Id,
            Action = AuditActionEnum.LoginSuccess,
            IsSuccess = true,
            CreatedAt = DateTime.UtcNow,
            IpAddress = "10.0.0.1"
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            tokenSeed: new[] { token },
            auditLogSeed: new[] { audit });
        var handler = new ExportMyDataQueryHandler(uow.Object);

        var resp = await handler.Handle(new ExportMyDataQuery { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Sessions.Should().HaveCount(1);
        resp.Data.Sessions[0].Id.Should().Be(token.Id);
        resp.Data.Sessions[0].IpAddress.Should().Be("10.0.0.1");
        resp.Data.AuditLogs.Should().HaveCount(1);
        resp.Data.AuditLogs[0].Action.Should().Be(AuditActionEnum.LoginSuccess.ToString());

        // RefreshToken.Token hash KHÔNG ĐƯỢC export (chỉ Id + metadata).
        var json = System.Text.Json.JsonSerializer.Serialize(resp.Data);
        json.Should().NotContain("HASH_DO_NOT_LEAK");
    }

    [Fact]
    public async Task Handle_WithProfileAndStaffProfile_IncludesBoth()
    {
        var account = SeedAccount();
        var profile = new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Address = "Hanoi",
            TimeZone = "Asia/Ho_Chi_Minh",
            AvatarSource = AvatarSourceEnum.None
        };
        var staffProfile = new StaffProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EmployeeCode = "STAFF-001",
            Department = "Maintenance",
            SkillTier = StaffSkillTierEnum.Generalist
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            accountProfileSeed: new[] { profile },
            staffProfileSeed: new[] { staffProfile });
        var handler = new ExportMyDataQueryHandler(uow.Object);

        var resp = await handler.Handle(new ExportMyDataQuery { AccountId = account.Id }, CancellationToken.None);

        resp.Data!.Profile.Should().NotBeNull();
        resp.Data.Profile!.Address.Should().Be("Hanoi");
        resp.Data.StaffProfile.Should().NotBeNull();
        resp.Data.StaffProfile!.EmployeeCode.Should().Be("STAFF-001");
    }

    [Fact]
    public async Task Handle_BackupCodes_OnlyMetadata_NotCodeHash()
    {
        var account = SeedAccount();
        var bc = new BackupCode
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            CodeHash = "HASHED_CODE_DO_NOT_LEAK",
            CreatedAt = DateTime.UtcNow
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            backupCodeSeed: new[] { bc });
        var handler = new ExportMyDataQueryHandler(uow.Object);

        var resp = await handler.Handle(new ExportMyDataQuery { AccountId = account.Id }, CancellationToken.None);

        resp.Data!.BackupCodes.Should().HaveCount(1);
        resp.Data.BackupCodes[0].Id.Should().Be(bc.Id);
        var json = System.Text.Json.JsonSerializer.Serialize(resp.Data);
        json.Should().NotContain("HASHED_CODE_DO_NOT_LEAK");
        json.Should().NotContain("CodeHash");
    }
}
