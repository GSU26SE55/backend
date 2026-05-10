using SharedContracts.Common.Responses;

namespace SharedInfrastructure.UnitTests.Contracts;

public class PaginationResponseTests
{
    [Theory]
    [InlineData(0, 10, 0)]   // empty
    [InlineData(10, 10, 1)]  // exactly 1 page
    [InlineData(11, 10, 2)]  // overflow → 2
    [InlineData(100, 10, 10)]
    [InlineData(101, 10, 11)]
    [InlineData(99, 100, 1)]
    public void TotalPages_CeilingDivision(int totalItems, int pageSize, int expectedPages)
    {
        var resp = new PaginationResponse<int> { TotalItems = totalItems, PageSize = pageSize };

        resp.TotalPages.Should().Be(expectedPages);
    }

    [Theory]
    [InlineData(1, 30, 10, true)]  // 3 pages, on first → has next
    [InlineData(2, 30, 10, true)]  // on middle
    [InlineData(3, 30, 10, false)] // on last
    [InlineData(1, 5, 10, false)]  // single page → no next
    public void HasNextPage_ChecksAgainstTotalPages(int currentPage, int totalItems, int pageSize, bool expected)
    {
        var resp = new PaginationResponse<int>
        {
            PageNumber = currentPage,
            TotalItems = totalItems,
            PageSize = pageSize
        };
        resp.HasNextPage.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(99, true)]
    public void HasPreviousPage_TrueWhenPageGreaterThan1(int currentPage, bool expected)
    {
        var resp = new PaginationResponse<int> { PageNumber = currentPage };
        resp.HasPreviousPage.Should().Be(expected);
    }

    [Fact]
    public void Items_DefaultsToEmptyList()
    {
        var resp = new PaginationResponse<string>();
        resp.Items.Should().NotBeNull().And.BeEmpty();
    }
}
