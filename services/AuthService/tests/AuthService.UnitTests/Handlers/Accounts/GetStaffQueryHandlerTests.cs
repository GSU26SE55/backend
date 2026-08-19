using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using FluentAssertions;

namespace AuthService.UnitTests.Handlers.Accounts;

public class GetStaffQueryHandlerTests
{
    [Theory]
    [InlineData("P1Critical")]
    [InlineData("Urgent")]
    public async Task Handle_SeniorPriority_ReturnsOnlyAvailableActiveSeniorSpecialists(string priority)
    {
        var eligibleAccount = CreateAccount("tier3@example.com", AccountStatusEnum.Active);
        var lowerTierAccount = CreateAccount("tier2@example.com", AccountStatusEnum.Active);
        var unavailableAccount = CreateAccount("unavailable@example.com", AccountStatusEnum.Active);

        var profiles = new[]
        {
            CreateProfile(eligibleAccount, true, StaffSkillTierEnum.SeniorSpecialist),
            CreateProfile(lowerTierAccount, true, StaffSkillTierEnum.ModuleSpecialist),
            CreateProfile(unavailableAccount, false, StaffSkillTierEnum.SeniorSpecialist)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { eligibleAccount, lowerTierAccount, unavailableAccount },
            staffProfileSeed: profiles);

        var response = await new GetStaffQueryHandler(uow.Object).Handle(
            new GetStaffQuery { TicketPriority = priority }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle();
        response.Data![0].AccountId.Should().Be(eligibleAccount.Id);
        response.Data[0].SkillTier.Should().Be((int)StaffSkillTierEnum.SeniorSpecialist);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task Handle_ExcludesNonStaffRoles_EvenWhenTheyHaveAStaffProfile(string roleName)
    {
        // UpdateStaffProfileCommandHandler / AddStaffSkillCommandHandler tạo StaffProfile cho bất kỳ
        // accountId nào tồn tại, không kiểm tra role. Gán tier cho một Manager một lần là họ hiện
        // trong dropdown "Primary Handler" của màn hình phân công ticket.
        var staffAccount = CreateAccount("staff@example.com", AccountStatusEnum.Active);
        var nonStaffAccount = CreateAccount("manager@example.com", AccountStatusEnum.Active, roleName);

        var profiles = new[]
        {
            CreateProfile(staffAccount, true, StaffSkillTierEnum.SeniorSpecialist),
            CreateProfile(nonStaffAccount, true, StaffSkillTierEnum.SeniorSpecialist)
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { staffAccount, nonStaffAccount },
            staffProfileSeed: profiles);

        var response = await new GetStaffQueryHandler(uow.Object).Handle(
            new GetStaffQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle();
        response.Data![0].AccountId.Should().Be(staffAccount.Id);
    }

    [Fact]
    public async Task Handle_InvalidPriority_ReturnsBadRequest()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();

        var response = await new GetStaffQueryHandler(uow.Object).Handle(
            new GetStaffQuery { TicketPriority = "P4" }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }

    private static Account CreateAccount(string email, AccountStatusEnum status, string roleName = "Staff")
    {
        var roleId = Guid.NewGuid();
        return new Account
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hash",
            FullName = email,
            Status = status,
            RoleId = roleId,
            Role = new Role
            {
                Id = roleId,
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            }
        };
    }

    private static StaffProfile CreateProfile(Account account, bool isAvailable, StaffSkillTierEnum tier) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = account.Id,
        Account = account,
        IsAvailable = isAvailable,
        SkillTier = tier
    };
}
