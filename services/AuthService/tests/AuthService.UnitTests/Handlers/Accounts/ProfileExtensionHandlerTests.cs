using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

public class ProfileExtensionHandlerTests
{
    [Fact]
    public async Task SetMyAvatar_StoresUploadedAvatar_AndDisplayAvatarUsesFileDownload()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "me@example.com",
            PasswordHash = "x",
            FullName = "Me",
            Status = AccountStatusEnum.Active,
            AccountRoles = new List<AccountRole>()
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new SetMyAvatarCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);
        var avatarFileId = Guid.NewGuid();

        var response = await handler.Handle(new SetMyAvatarCommand
        {
            AccountId = account.Id,
            AvatarFileId = avatarFileId
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Profile!.AvatarFileId.Should().Be(avatarFileId);
        response.Data.Profile.AvatarSource.Should().Be(AvatarSourceEnum.Uploaded);
        response.Data.DisplayAvatarUrl.Should().Be($"/api/files/{avatarFileId}/download");
    }

    [Fact]
    public async Task UpdateMyProfile_UpdatesExtensionTable_AndLegacyAccountFields()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "me@example.com",
            PasswordHash = "x",
            FullName = "Old",
            Status = AccountStatusEnum.Active,
            AccountRoles = new List<AccountRole>()
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UpdateMyProfileCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);
        var birthDate = new DateTime(1998, 4, 2);

        var response = await handler.Handle(new UpdateMyProfileCommand
        {
            AccountId = account.Id,
            FullName = "New Name",
            PhoneNumber = "0900123456",
            Address = "District 1",
            BirthDate = birthDate,
            TimeZone = "Asia/Ho_Chi_Minh"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        account.FullName.Should().Be("New Name");
        account.PhoneNumber.Should().Be("0900123456");
        account.Address.Should().Be("District 1");
        account.DateOfBirth.Should().Be(birthDate);
        response.Data!.Profile!.Address.Should().Be("District 1");
        response.Data.Profile.BirthDate.Should().Be(birthDate);
        response.Data.Profile.TimeZone.Should().Be("Asia/Ho_Chi_Minh");
    }

    [Fact]
    public async Task GetStaff_FilterBySkill_ReturnsMatchingAssignmentProfiles()
    {
        var matchingAccount = new Account
        {
            Id = Guid.NewGuid(),
            Email = "staff@example.com",
            PasswordHash = "x",
            FullName = "Staff A",
            Status = AccountStatusEnum.Active
        };
        var otherAccount = new Account
        {
            Id = Guid.NewGuid(),
            Email = "other@example.com",
            PasswordHash = "x",
            FullName = "Staff B",
            Status = AccountStatusEnum.Active
        };
        var matchingProfile = new StaffProfile
        {
            Id = Guid.NewGuid(),
            AccountId = matchingAccount.Id,
            Account = matchingAccount,
            Department = "Ops",
            IsAvailable = true,
            MaxConcurrentTickets = 4,
            Skills = new List<StaffSkill>
            {
                new() { Id = Guid.NewGuid(), StaffAccountId = matchingAccount.Id, SkillCode = "LiFePO4", SkillLevel = 4 }
            }
        };
        var otherProfile = new StaffProfile
        {
            Id = Guid.NewGuid(),
            AccountId = otherAccount.Id,
            Account = otherAccount,
            Department = "Ops",
            IsAvailable = true,
            MaxConcurrentTickets = 4,
            Skills = new List<StaffSkill>
            {
                new() { Id = Guid.NewGuid(), StaffAccountId = otherAccount.Id, SkillCode = "LeadAcid", SkillLevel = 3 }
            }
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { matchingAccount, otherAccount },
            staffProfileSeed: new[] { matchingProfile, otherProfile });
        var handler = new GetStaffQueryHandler(uow.Object);

        var response = await handler.Handle(new GetStaffQuery { Skill = "LiFePO4" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle();
        response.Data![0].AccountId.Should().Be(matchingAccount.Id);
        response.Data[0].Skills.Should().ContainSingle(skill => skill.SkillCode == "LiFePO4");
    }
}
