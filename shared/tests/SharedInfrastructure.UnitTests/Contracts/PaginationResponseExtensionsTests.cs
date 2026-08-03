using SharedContracts.Common.Responses;

namespace SharedInfrastructure.UnitTests.Contracts;

public class PaginationResponseExtensionsTests
{
    private static PaginationResponse<int> Page() => new()
    {
        Items = new List<int> { 1, 2, 3 },
        TotalItems = 42,
        PageNumber = 3,
        PageSize = 3,
    };

    [Fact]
    public void Map_ChangesItemType_AndKeepsPagingMetadata()
    {
        var mapped = Page().Map(i => $"#{i}");

        mapped.Items.Should().Equal("#1", "#2", "#3");
        mapped.TotalItems.Should().Be(42);
        mapped.PageNumber.Should().Be(3);
        mapped.PageSize.Should().Be(3);
        // Suy ra từ metadata — nếu Map làm rơi TotalItems thì 3 dòng dưới đây sai ngay.
        mapped.TotalPages.Should().Be(14);
        mapped.HasNextPage.Should().BeTrue();
        mapped.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void WithItems_ReplacesWholeList_AndKeepsPagingMetadata()
    {
        var replaced = Page().WithItems(new List<string> { "a", "b" });

        replaced.Items.Should().Equal("a", "b");
        replaced.TotalItems.Should().Be(42);
        replaced.PageNumber.Should().Be(3);
        replaced.PageSize.Should().Be(3);
    }

    [Fact]
    public void Map_EmptyPage_StaysEmpty_ButKeepsTotals()
    {
        var empty = new PaginationResponse<int> { Items = new List<int>(), TotalItems = 42, PageNumber = 99, PageSize = 10 };

        var mapped = empty.Map(i => i.ToString());

        mapped.Items.Should().BeEmpty();
        mapped.TotalItems.Should().Be(42);
        mapped.PageNumber.Should().Be(99);
    }
}
