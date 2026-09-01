using AuthService.Domain.Enums;
using FluentAssertions;

namespace AuthService.UnitTests.Contracts;

public class AccountStatusEnumContractTests
{
    [Fact]
    public void NumericValues_MatchPersistedDatabaseAndIntegrationEventContract()
    {
        ((int)AccountStatusEnum.PendingVerification).Should().Be(0);
        ((int)AccountStatusEnum.Active).Should().Be(1);
        ((int)AccountStatusEnum.Locked).Should().Be(2);
        ((int)AccountStatusEnum.Inactive).Should().Be(3);
        ((int)AccountStatusEnum.Suspended).Should().Be(4);
        ((int)AccountStatusEnum.Banned).Should().Be(5);
    }
}
