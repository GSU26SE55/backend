using AuthService.Application.CQRS.Command.Role;
using AuthService.Domain.Enums;

namespace AuthService.UnitTests.Validators;

public class CreateRoleCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new CreateRoleCommand { Name = "Inspector", Description = "X" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyName_Fails(string name)
    {
        var r = await new CreateRoleCommand { Name = name }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task Name_TooLong_Fails()
    {
        var r = await new CreateRoleCommand { Name = new string('a', 101) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task Description_TooLong_Fails()
    {
        var r = await new CreateRoleCommand { Name = "OK", Description = new string('a', 501) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Description");
    }
}

public class UpdateRoleCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new UpdateRoleCommand { Id = Guid.NewGuid(), Name = "X", Description = "y" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails() =>
        (await new UpdateRoleCommand { Id = Guid.Empty, Name = "X" }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task EmptyName_Fails() =>
        (await new UpdateRoleCommand { Id = Guid.NewGuid(), Name = "" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class ChangeRoleStatusCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new ChangeRoleStatusCommand { Id = Guid.NewGuid(), Status = RoleStatusEnum.Active }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails() =>
        (await new ChangeRoleStatusCommand { Id = Guid.Empty, Status = RoleStatusEnum.Active }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task UndefinedStatus_Fails() =>
        (await new ChangeRoleStatusCommand { Id = Guid.NewGuid(), Status = (RoleStatusEnum)999 }.ValidateAsync()).IsSuccess.Should().BeFalse();
}
