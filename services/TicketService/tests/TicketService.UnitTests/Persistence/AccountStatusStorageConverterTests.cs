using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence.Converters;

namespace TicketService.UnitTests.Persistence;

public class AccountStatusStorageConverterTests
{
    private readonly AccountStatusStorageConverter _converter = new();

    [Theory]
    [InlineData(AccountStatusEnum.PendingVerification, 1)]
    [InlineData(AccountStatusEnum.Active, 2)]
    [InlineData(AccountStatusEnum.Locked, 3)]
    [InlineData(AccountStatusEnum.Inactive, 4)]
    [InlineData(AccountStatusEnum.Suspended, 5)]
    [InlineData(AccountStatusEnum.Banned, 6)]
    public void ConvertToProvider_PreservesLegacyDatabaseContract(
        AccountStatusEnum domainStatus,
        int expectedStoredValue)
    {
        var convert = _converter.ConvertToProviderExpression.Compile();

        convert(domainStatus).Should().Be(expectedStoredValue);
    }

    [Theory]
    [InlineData(1, AccountStatusEnum.PendingVerification)]
    [InlineData(2, AccountStatusEnum.Active)]
    [InlineData(3, AccountStatusEnum.Locked)]
    [InlineData(4, AccountStatusEnum.Inactive)]
    [InlineData(5, AccountStatusEnum.Suspended)]
    [InlineData(6, AccountStatusEnum.Banned)]
    public void ConvertFromProvider_ReadsExistingRowsWithAlignedDomainSemantics(
        int storedValue,
        AccountStatusEnum expectedDomainStatus)
    {
        var convert = _converter.ConvertFromProviderExpression.Compile();

        convert(storedValue).Should().Be(expectedDomainStatus);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(99)]
    public void ConvertFromProvider_UnknownValue_FailsClosedAsInactive(int storedValue)
    {
        var convert = _converter.ConvertFromProviderExpression.Compile();

        convert(storedValue).Should().Be(AccountStatusEnum.Inactive);
    }
}
