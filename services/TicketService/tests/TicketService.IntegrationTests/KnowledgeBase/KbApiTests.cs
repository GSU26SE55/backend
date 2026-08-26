using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.KnowledgeBase;

public class KbApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly TicketApiFactory _factory;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

    public KbApiTests(TicketApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
        _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        // Clean and recreate test database
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task CreateKbArticle_ValidData_Returns201AndActionDto()
    {
        // Arrange
        var command = new CreateKbArticleCommand
        {
            Title = "Battery Overheat Maintenance Steps",
            Content = "Battery temperature exceeds 50C during charging. Verify cooling fan operation. Replace cooling fan if dead.",
            Category = TicketCategoryEnum.Overheat,
            Tags = new List<string> { "battery", "overheat" },
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/internal/knowledge-base", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleActionDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetKbArticleList_ReturnsOnlyNonDeletedArticles()
    {
        // Setup seed database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-001",
                Title = "Published Article",
                Content = JsonDocument.Parse("\"Troubleshoot charging\""),
                Status = KbArticleStatusEnum.Published,
                IsDeleted = false
            });
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-002",
                Title = "Deleted Article",
                Content = JsonDocument.Parse("\"Troubleshoot leaking\""),
                Status = KbArticleStatusEnum.Published,
                IsDeleted = true
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync("/api/knowledge-base");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<PaginationResponse<KbArticleListItemDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().Title.Should().Be("Published Article");
    }

    [Fact]
    public async Task GetKbArticleById_ValidId_ReturnsArticleDto()
    {
        // Setup seed database
        var articleId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = articleId,
                Code = "KB-003",
                Title = "Get By Id Article",
                Content = JsonDocument.Parse("\"Symptom check\""),
                Status = KbArticleStatusEnum.Published,
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/knowledge-base/{articleId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Title.Should().Be("Get By Id Article");
    }

    /// <summary>
    /// Tác giả sửa bài của chính mình vẫn phải qua phê duyệt.
    /// </summary>
    /// <remarks>
    /// Bài kiểm tra này từng khẳng định điều ngược lại — tác giả ghi thẳng, không qua duyệt —
    /// theo commit <c>0e68b2a6</c>. Commit <c>267064b6</c> gỡ nhánh <c>isCreator</c> khỏi
    /// <c>UpdateKbArticleCommandHandler</c> vì lý do nghiệp vụ: nếu tác giả tự đẩy được nội dung
    /// lên một bài đã Published thì không ai rà lại một sửa đổi sai. Commit đó có cập nhật unit
    /// test nhưng bỏ sót bài này, nên nó đỏ từ lúc ấy. Nay nó khẳng định đúng quy tắc đang chạy,
    /// và là chỗ duy nhất phủ được đường "tác giả sửa bài của chính mình" — unit test tương ứng
    /// cố tình dựng bài của người khác.
    /// </remarks>
    [Fact]
    public async Task UpdateKbArticle_ByCreator_StillGoesThroughReview()
    {
        // Setup seed database — bài viết do chính người đang đăng nhập tạo.
        var articleId = Guid.NewGuid();
        var creatorId = Guid.Parse(TestAuthHandler.UserId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = articleId,
                Code = "KB-004",
                Title = "Old Title",
                Content = JsonDocument.Parse("\"Old Symptoms\""),
                Status = KbArticleStatusEnum.Published,
                CreatedByUserId = creatorId,
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            Title = "New Title",
            Content = "New Symptoms. New Steps. New Solution.",
            Category = TicketCategoryEnum.Overheat,
            Tags = new List<string> { "battery" },
        };

        // Act — Staff là tác giả của bài.
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/knowledge-base/{articleId}")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        // Assert — chuyển sang chờ duyệt dù người sửa chính là người viết ra bài.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.PendingReview);

        // Bài gốc giữ nguyên nội dung cũ; nội dung mới nằm trong bản version chờ duyệt.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var article = await db.KnowledgeBaseArticles.FirstAsync(a => a.Id == articleId);
            article.Title.Should().Be("Old Title");
            article.ReviewRequired.Should().BeTrue();
            article.PendingReviewBy.Should().Be(creatorId);
        }
    }

    /// <summary>
    /// Manager ghi thẳng, không qua phê duyệt — đường duy nhất còn lại sau commit <c>267064b6</c>.
    /// </summary>
    [Fact]
    public async Task UpdateKbArticle_ByManager_AppliesContentDirectly()
    {
        var articleId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = articleId,
                Code = "KB-004C",
                Title = "Old Title",
                Content = JsonDocument.Parse("\"Old Symptoms\""),
                Status = KbArticleStatusEnum.Published,
                CreatedByUserId = Guid.NewGuid(),
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            Title = "New Title",
            Content = "New Symptoms. New Steps. New Solution.",
            Category = TicketCategoryEnum.Overheat,
            Tags = new List<string> { "battery" },
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/knowledge-base/{articleId}")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Manager");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().NotBe(KbArticleStatusEnum.PendingReview);
        result.Data!.Title.Should().Be("New Title");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var article = await db.KnowledgeBaseArticles.FirstAsync(a => a.Id == articleId);
            article.Title.Should().Be("New Title");
            article.ReviewRequired.Should().BeFalse();
            // Bài đang Published phải trở lại Published, không kẹt ở trạng thái chờ duyệt.
            article.Status.Should().Be(KbArticleStatusEnum.Published);
            article.PendingReviewBy.Should().BeNull();
        }
    }

    [Fact]
    public async Task UpdateKbArticle_ByNonCreatorStaff_Returns200AndPendingStatus()
    {
        // Setup seed database — bài viết của người khác, Staff sửa phải chờ duyệt.
        var articleId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = articleId,
                Code = "KB-004B",
                Title = "Old Title",
                Content = JsonDocument.Parse("\"Old Symptoms\""),
                Status = KbArticleStatusEnum.Published,
                CreatedByUserId = Guid.NewGuid(),
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            Title = "New Title",
            Content = "New Symptoms. New Steps. New Solution.",
            Category = TicketCategoryEnum.Overheat,
            Tags = new List<string> { "battery" },
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/knowledge-base/{articleId}")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");

        // Act
        var response = await _client.SendAsync(request);

        // Assert — thay đổi được lưu thành version chờ duyệt, article giữ nội dung cũ.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.PendingReview);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var article = await db.KnowledgeBaseArticles.FirstAsync(a => a.Id == articleId);
            article.Title.Should().Be("Old Title");
            article.ReviewRequired.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MarkHelpful_IncrementsHelpfulCountOnDb()
    {
        // Setup seed database
        var articleId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.KnowledgeBaseArticles.Add(new KnowledgeBaseArticle
            {
                Id = articleId,
                Code = "KB-005",
                Title = "Helpful Article",
                Content = JsonDocument.Parse("\"Some issues\""),
                Status = KbArticleStatusEnum.Published,
                HelpfulCount = 5,
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync($"/api/knowledge-base/{articleId}/helpful", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check DB value
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var article = await db.KnowledgeBaseArticles.FindAsync(articleId);
            article!.HelpfulCount.Should().Be(6);
        }
    }
}
