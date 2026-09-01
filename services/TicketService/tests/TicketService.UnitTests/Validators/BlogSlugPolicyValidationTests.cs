using TicketService.Application.CQRS.Command.Blog;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Slug đi thẳng vào URL nên phải đúng định dạng. Trước đây chỉ FE kiểm tra, BE cho qua —
/// gọi thẳng API là lưu được slug có dấu cách/chữ hoa, và bài đó sau này không sửa được từ
/// editor vì slug lấy ra không qua nổi validate của FE. Update còn thiếu cả cap 300 mà
/// Create đã có.
/// </summary>
public class BlogSlugPolicyValidationTests
{
    private static CreateBlogPostCommand ValidCreate() => new()
    {
        Title = "Bao duong pin dinh ky",
        Slug = "bao-duong-pin-dinh-ky",
        Summary = "Tom tat",
        ContentHtml = "<p>Noi dung</p>"
    };

    private static UpdateBlogPostCommand ValidUpdate() => new()
    {
        BlogPostId = Guid.NewGuid(),
        Title = "Bao duong pin dinh ky",
        Slug = "bao-duong-pin-dinh-ky",
        Summary = "Tom tat",
        ContentHtml = "<p>Noi dung</p>",
        CurrentVersion = 1
    };

    [Fact]
    public async Task ValidCreate_Passes()
        => (await ValidCreate().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task ValidUpdate_Passes()
        => (await ValidUpdate().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("Bai Viet 2026")]      // dấu cách + chữ hoa
    [InlineData("bai--viet")]          // gạch nối đôi
    [InlineData("-bai-viet")]          // gạch nối đầu
    [InlineData("bai-viet-")]          // gạch nối cuối
    [InlineData("bài-viết")]           // dấu tiếng Việt
    [InlineData("bai_viet")]           // gạch dưới
    public async Task BadSlug_FailsOnCreate(string slug)
    {
        var c = ValidCreate();
        c.Slug = slug;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug");
    }

    [Theory]
    [InlineData("Bai Viet 2026")]
    [InlineData("bai--viet")]
    [InlineData("bài-viết")]
    public async Task BadSlug_FailsOnUpdate(string slug)
    {
        var c = ValidUpdate();
        c.Slug = slug;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("bai-viet-2026")]
    [InlineData("2026")]
    public async Task GoodSlug_Passes(string slug)
    {
        var c = ValidCreate();
        c.Slug = slug;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Slug");
    }

    /// <summary>Update trước đây thiếu hẳn cap 300 mà Create đã có.</summary>
    [Fact]
    public async Task SlugTooLong_FailsOnUpdate()
    {
        var c = ValidUpdate();
        c.Slug = new string('a', 301);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug");
    }
}
