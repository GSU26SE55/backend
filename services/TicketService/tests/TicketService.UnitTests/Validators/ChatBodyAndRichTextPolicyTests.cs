using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Hai luật từng chỉ áp ở một nhánh:
/// - Chặn nội dung toàn khoảng trắng/emoji chỉ có ở đường tạo comment, hai đường edit thì không
///   → post "hello" rồi sửa thành "👍👍" là lách được luật mà đường tạo cấm.
/// - "Có nội dung" ở BE chỉ là IsNullOrWhiteSpace trên HTML thô, nên "&lt;hr&gt;" được chấp nhận
///   trong khi FE strip tag rồi mới kiểm nên coi là rỗng → bài lưu qua API không mở sửa lại được.
/// </summary>
public class ChatBodyAndRichTextPolicyTests
{
    private static ChatEditCommand Edit(string body) => new()
    {
        ChatId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Body = body
    };

    private static ChatOverrideEditCommand OverrideEdit(string body) => new()
    {
        ChatId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Body = body,
        OverrideReason = "Sửa theo yêu cầu khách"
    };

    private static ChatAddCommand Add(string body) => new()
    {
        TicketId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Body = body
    };

    [Theory]
    [InlineData("👍👍")]
    [InlineData("   ")]
    [InlineData("→ ✔")]
    public async Task EmojiOrWhitespaceOnly_RejectedOnEveryPath(string body)
    {
        (await Add(body).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
        (await Edit(body).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
        (await OverrideEdit(body).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
    }

    [Fact]
    public async Task RealText_AcceptedOnEveryPath()
    {
        (await Add("Đã kiểm tra pin 👍").ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Body");
        (await Edit("Đã kiểm tra pin 👍").ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Body");
        (await OverrideEdit("Đã kiểm tra pin 👍").ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Body");
    }

    [Fact]
    public async Task OverlongBody_RejectedOnEveryPath()
    {
        var tooLong = new string('a', 10001);

        (await Add(tooLong).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
        (await Edit(tooLong).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
        (await OverrideEdit(tooLong).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Body");
    }

    // ---- RichTextPolicy ----

    private static CreateBlogPostCommand Post(string contentHtml) => new()
    {
        Title = "Bao duong pin",
        Slug = "bao-duong-pin",
        Summary = "Tom tat",
        ContentHtml = contentHtml
    };

    [Theory]
    [InlineData("<hr>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p></p>")]
    [InlineData("<div><span></span></div>")]
    public async Task HtmlWithoutRealContent_IsRejected(string html)
        => (await Post(html).ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentHtml");

    [Theory]
    [InlineData("<p>Noi dung that</p>")]
    [InlineData("<figure><img src=\"x.png\"></figure>")]
    [InlineData("<video src=\"x.mp4\"></video>")]
    public async Task HtmlWithTextOrMedia_IsAccepted(string html)
        => (await Post(html).ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "ContentHtml");

    [Fact]
    public async Task Summary_IsCappedAt1000()
    {
        var c = Post("<p>ok</p>");
        c.Summary = new string('s', 1001);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Summary");
    }

    [Fact]
    public async Task KbArticle_UsesTheSameContentRule()
    {
        var c = new CreateKbArticleCommand
        {
            Title = "Huong dan",
            Content = "<hr>",
            Category = TicketCategoryEnum.Other
        };

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Content");
    }

    /// <summary>Cap 50000 của KB không được mất khi thay luật "có nội dung".</summary>
    [Fact]
    public async Task KbArticle_StillEnforcesMaxLength()
    {
        var c = new CreateKbArticleCommand
        {
            Title = "Huong dan",
            Content = "<p>" + new string('a', 50001) + "</p>",
            Category = TicketCategoryEnum.Other
        };

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Content");
    }
}
