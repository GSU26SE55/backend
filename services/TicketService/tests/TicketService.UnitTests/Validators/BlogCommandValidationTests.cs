using TicketService.Application.CQRS.Command.Blog;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Luật validate của nhóm Blog. Nội dung bài viết do người dùng nhập và được publish ra ngoài,
/// nên các luật bắt buộc/độ dài ở đây là hàng rào đầu tiên trước khi dữ liệu vào DB.
/// </summary>
public class CreateBlogPostCommandValidationTests
{
    private static CreateBlogPostCommand Valid() => new()
    {
        Title = "Battery maintenance checklist",
        Slug = "battery-maintenance-checklist",
        Summary = "A short summary of the checklist.",
        ContentHtml = "<p>Full content.</p>",
        CurrentUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingTitle_Fails(string title)
    {
        var c = Valid();
        c.Title = title;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Title");
    }

    [Fact]
    public async Task TitleTooLong_Fails()
    {
        var c = Valid();
        c.Title = new string('t', 257);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Title"
            && e.Detail.Contains("256"));
    }

    [Fact]
    public async Task TitleExactly256_Passes()
    {
        var c = Valid();
        c.Title = new string('t', 256);

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Title");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task MissingSlug_Fails(string slug)
    {
        var c = Valid();
        c.Slug = slug;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug");
    }

    [Fact]
    public async Task SlugTooLong_Fails()
    {
        var c = Valid();
        c.Slug = new string('s', 301);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug"
            && e.Detail.Contains("300"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingSummary_Fails(string summary)
    {
        var c = Valid();
        c.Summary = summary;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Summary");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingContent_Fails(string content)
    {
        var c = Valid();
        c.ContentHtml = content;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentHtml");
    }

    /// <summary>BlogTemplateId là tuỳ chọn — bài viết có thể tạo không theo mẫu nào.</summary>
    [Fact]
    public async Task NullTemplateId_Passes()
    {
        var c = Valid();
        c.BlogTemplateId = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}

public class UpdateBlogPostCommandValidationTests
{
    private static UpdateBlogPostCommand Valid() => new()
    {
        BlogPostId = Guid.NewGuid(),
        Title = "Updated title",
        Slug = "updated-title",
        Summary = "Updated summary.",
        ContentHtml = "<p>Updated content.</p>",
        ChangeNote = "Fixed a typo",
        CurrentVersion = 3,
        CurrentUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyBlogPostId_Fails()
    {
        var c = Valid();
        c.BlogPostId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "BlogPostId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingTitle_Fails(string title)
    {
        var c = Valid();
        c.Title = title;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Title");
    }

    [Fact]
    public async Task TitleTooLong_Fails()
    {
        var c = Valid();
        c.Title = new string('t', 257);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Title");
    }

    [Fact]
    public async Task MissingSlug_Fails()
    {
        var c = Valid();
        c.Slug = "";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Slug");
    }

    [Fact]
    public async Task MissingSummary_Fails()
    {
        var c = Valid();
        c.Summary = "  ";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Summary");
    }

    [Fact]
    public async Task MissingContent_Fails()
    {
        var c = Valid();
        c.ContentHtml = "";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentHtml");
    }

    /// <summary>
    /// CurrentVersion phục vụ optimistic concurrency — số &lt;= 0 nghĩa là client không gửi
    /// phiên bản đang sửa, và cập nhật mù như vậy có thể ghi đè thay đổi của người khác.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveVersion_Fails(int version)
    {
        var c = Valid();
        c.CurrentVersion = version;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "CurrentVersion");
    }

    /// <summary>ChangeNote là tuỳ chọn.</summary>
    [Fact]
    public async Task NullChangeNote_Passes()
    {
        var c = Valid();
        c.ChangeNote = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}

public class BlogTemplateCommandValidationTests
{
    private static CreateBlogTemplateCommand ValidCreate() => new()
    {
        Name = "Incident report template",
        Description = "Used for post-incident write-ups.",
        ContentHtml = "<h1>{{title}}</h1>",
        CurrentUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Create_Valid_Passes() => (await ValidCreate().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingName_Fails(string name)
    {
        var c = ValidCreate();
        c.Name = name;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task Create_NameTooLong_Fails()
    {
        var c = ValidCreate();
        c.Name = new string('n', 201);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Name"
            && e.Detail.Contains("200"));
    }

    [Fact]
    public async Task Create_MissingContent_Fails()
    {
        var c = ValidCreate();
        c.ContentHtml = "";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentHtml");
    }

    /// <summary>Description không có luật — bỏ trống vẫn hợp lệ.</summary>
    [Fact]
    public async Task Create_EmptyDescription_Passes()
    {
        var c = ValidCreate();
        c.Description = "";

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    private static UpdateBlogTemplateCommand ValidUpdate() => new()
    {
        TemplateId = Guid.NewGuid(),
        Name = "Incident report template v2",
        Description = "Revised.",
        ContentHtml = "<h1>{{title}}</h1>",
        IsActive = true
    };

    [Fact]
    public async Task Update_Valid_Passes() => (await ValidUpdate().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Update_EmptyTemplateId_Fails()
    {
        var c = ValidUpdate();
        c.TemplateId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TemplateId");
    }

    [Fact]
    public async Task Update_MissingName_Fails()
    {
        var c = ValidUpdate();
        c.Name = "  ";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task Update_NameTooLong_Fails()
    {
        var c = ValidUpdate();
        c.Name = new string('n', 201);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task Update_MissingContent_Fails()
    {
        var c = ValidUpdate();
        c.ContentHtml = "";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentHtml");
    }

    [Fact]
    public async Task Delete_ValidId_Passes()
    {
        var r = await new DeleteBlogTemplateCommand { TemplateId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_EmptyId_Fails()
    {
        var r = await new DeleteBlogTemplateCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TemplateId");
    }
}

/// <summary>
/// Các lệnh Blog chỉ mang một id. Luật đơn nhưng vẫn cần chạy: đây là các thao tác đổi trạng thái
/// công khai (publish/archive) và xoá.
/// </summary>
public class BlogPostIdOnlyCommandValidationTests
{
    [Fact]
    public async Task Publish_ValidId_Passes()
    {
        var r = await new PublishBlogPostCommand { BlogPostId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_EmptyId_Fails()
    {
        var r = await new PublishBlogPostCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "BlogPostId");
    }

    [Fact]
    public async Task Archive_ValidId_Passes()
    {
        var r = await new ArchiveBlogPostCommand { BlogPostId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Archive_EmptyId_Fails()
    {
        var r = await new ArchiveBlogPostCommand().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "BlogPostId");
    }

    [Fact]
    public async Task Delete_ValidId_Passes()
    {
        var r = await new DeleteBlogPostCommand { BlogPostId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_EmptyId_Fails()
    {
        var r = await new DeleteBlogPostCommand().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "BlogPostId");
    }

    [Fact]
    public async Task GenerateFromKb_ValidId_Passes()
    {
        var r = await new GenerateBlogFromKbCommand
        {
            KbArticleId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateFromKb_EmptyId_Fails()
    {
        var r = await new GenerateBlogFromKbCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "KbArticleId");
    }
}
