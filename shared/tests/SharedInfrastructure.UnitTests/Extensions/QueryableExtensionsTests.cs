using MockQueryable.Moq;
using SharedInfrastructure.Extensions;

namespace SharedInfrastructure.UnitTests.Extensions;

public class QueryableExtensionsTests
{
    private class Item
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
    }

    private static List<Item> Seed(int count)
        => Enumerable.Range(1, count)
            .Select(i => new Item { Id = Guid.NewGuid(), Name = $"item-{i}", Order = i })
            .ToList();

    [Fact]
    public async Task ToPagedEntityListAsync_FirstPage_ReturnsItemsAndCount()
    {
        var src = Seed(25).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 1, pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.TotalItems.Should().Be(25);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.Items.First().Order.Should().Be(1);
    }

    [Fact]
    public async Task ToPagedEntityListAsync_LastPage_ReturnsRemainder()
    {
        var src = Seed(25).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 3, pageSize: 10);

        result.Items.Should().HaveCount(5);
        result.Items.First().Order.Should().Be(21);
    }

    [Fact]
    public async Task ToPagedEntityListAsync_PageNumberZero_NormalizesTo1()
    {
        var src = Seed(5).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 0, pageSize: 10);

        result.PageNumber.Should().Be(1);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task ToPagedEntityListAsync_PageSizeZero_NormalizesTo10()
    {
        var src = Seed(15).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 1, pageSize: 0);

        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCount(10);
    }

    [Fact]
    public async Task ToPagedEntityListAsync_PageBeyondData_ReturnsEmptyPage_NotError()
    {
        var src = Seed(5).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 99, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(5);
        result.PageNumber.Should().Be(99);
        result.HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// Trước 02/08/2026 phép nhân chạy bằng int nên (pageNumber-1)*pageSize tràn và quấn thành số ÂM,
    /// đẩy xuống Postgres thành "OFFSET must not be negative" → HTTP 500 trên 7 endpoint đang chạy.
    /// 300000000 chọn có chủ đích: (300000000-1)*10 quấn thành -1294967306.
    /// </summary>
    [Theory]
    [InlineData(300000000, 10)]
    [InlineData(250000000, 10)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task ToPagedEntityListAsync_HugePageNumber_DoesNotOverflowIntoNegativeSkip(int pageNumber, int pageSize)
    {
        var src = Seed(5).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber, pageSize);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(5);
    }

    [Fact]
    public async Task ToPagedEntityListAsync_EmptySource_ReturnsEmptyPage()
    {
        var src = Seed(0).AsQueryable().BuildMock();

        var result = await src.ToPagedEntityListAsync(pageNumber: 1, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    /// <summary>Không trang nào được chồng lấn hay bỏ sót phần tử khi duyệt hết.</summary>
    [Fact]
    public async Task ToPagedEntityListAsync_WalkingAllPages_CoversEveryItemExactlyOnce()
    {
        var seed = Seed(23);
        var seen = new List<int>();

        for (var page = 1; page <= 8; page++)
        {
            var result = await seed.AsQueryable().BuildMock().ToPagedEntityListAsync(page, 3);
            seen.AddRange(result.Items.Select(i => i.Order));
        }

        seen.Should().HaveCount(23);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(seed.Select(i => i.Order));
    }

    [Fact]
    public async Task ToPagedListAsync_AppliesMapper_AndShapesFields()
    {
        var src = Seed(3).AsQueryable().BuildMock();

        var result = await src.ToPagedListAsync(
            pageNumber: 1,
            pageSize: 10,
            mapper: i => new { i.Id, i.Name, i.Order, Extra = "ignored" },
            fields: "Name,Order");

        result.Items.Should().HaveCount(3);
        result.TotalItems.Should().Be(3);

        var firstShaped = result.Items.First() as IDictionary<string, object>;
        firstShaped.Should().NotBeNull();
        firstShaped!.Should().HaveCount(2);
        firstShaped.Should().ContainKey("Name");
        firstShaped.Should().ContainKey("Order");
        firstShaped.Should().NotContainKey("Id");
        firstShaped.Should().NotContainKey("Extra");
    }

    [Fact]
    public async Task ToPagedListAsync_NullFields_KeepsFullDto()
    {
        var src = Seed(2).AsQueryable().BuildMock();

        var result = await src.ToPagedListAsync(
            pageNumber: 1,
            pageSize: 10,
            mapper: i => new { i.Id, i.Name },
            fields: null);

        // Khi fields=null, ShapeData trả về anonymous object — KHÔNG là dictionary.
        var first = result.Items.First();
        (first is IDictionary<string, object>).Should().BeFalse();
    }
}
