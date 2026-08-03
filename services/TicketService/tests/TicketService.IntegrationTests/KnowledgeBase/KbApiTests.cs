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

    [Fact]
    public async Task UpdateKbArticle_ByCreator_AppliesContentDirectly()
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

        // Act
        var response = await _client.PutAsJsonAsync($"/api/internal/knowledge-base/{articleId}", command);

        // Assert — chủ sở hữu cập nhật trực tiếp, không chuyển sang chờ duyệt.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CommonResponse<KbArticleDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().NotBe(KbArticleStatusEnum.PendingReview);
        result.Data!.Title.Should().Be("New Title");

        // Nội dung mới phải được ghi thẳng vào article trong DB.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var article = await db.KnowledgeBaseArticles.FirstAsync(a => a.Id == articleId);
            article.Title.Should().Be("New Title");
            article.ReviewRequired.Should().BeFalse();
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
