using SharedContracts.Common.Requests;

namespace SharedInfrastructure.UnitTests.Contracts;

public class PaginationRequestTests
{
    [Fact]
    public void Defaults_PageNumber1_PageSize10()
    {
        var req = new PaginationRequest();

        req.PageNumber.Should().Be(1);
        req.PageSize.Should().Be(10);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(-100, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10000, 10000)]
    public void PageNumber_NonPositive_NormalizesTo1(int input, int expected)
    {
        var req = new PaginationRequest { PageNumber = input };
        req.PageNumber.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(99999, 100)]
    public void PageSize_ClampedTo1Through100(int input, int expected)
    {
        var req = new PaginationRequest { PageSize = input };
        req.PageSize.Should().Be(expected);
    }
}
