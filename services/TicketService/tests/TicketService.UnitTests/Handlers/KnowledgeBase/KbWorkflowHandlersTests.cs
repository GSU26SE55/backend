using System.Text.Json;
using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Handler.KnowledgeBase;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.KnowledgeBase;

public class KbWorkflowHandlersTests
{
    [Fact]
    public async Task Handle_PublishCommand_UpdatesStatusToPublished()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.Draft
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;

        var handler = new PublishKbArticleCommandHandler(uow.Object);
        var command = new PublishKbArticleCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.Published);
        kbArticles.Verify(x => x.UpdateAsync(It.Is<KnowledgeBaseArticle>(a => a.Status == KbArticleStatusEnum.Published)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveReview_UpdatesStatusToDraft()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.PendingReview,
            ReviewRequired = true
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new ApproveReviewCommandHandler(uow.Object);
        var command = new ApproveReviewCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.Draft);
        article.ReviewRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PublishCommand_FromPendingReview_Returns409()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.PendingReview
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new PublishKbArticleCommandHandler(uow.Object);
        var command = new PublishKbArticleCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ArchiveCommand_UpdatesStatusToArchived()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, Status = KbArticleStatusEnum.Published };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;

        var handler = new ArchiveKbArticleCommandHandler(uow.Object);
        var command = new ArchiveKbArticleCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.Archived);
        kbArticles.Verify(x => x.UpdateAsync(It.Is<KnowledgeBaseArticle>(a => a.Status == KbArticleStatusEnum.Archived)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeleteCommand_SetsIsDeletedToTrue()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, IsDeleted = false };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;

        var handler = new DeleteKbArticleCommandHandler(uow.Object);
        var command = new DeleteKbArticleCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.IsDeleted.Should().BeTrue();
        kbArticles.Verify(x => x.UpdateAsync(It.Is<KnowledgeBaseArticle>(a => a.IsDeleted)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MarkHelpfulCommand_IncrementsHelpfulCount()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, HelpfulCount = 10 };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;

        var handler = new MarkHelpfulCommandHandler(uow.Object);
        var command = new MarkHelpfulCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.HelpfulCount.Should().Be(11);
        kbArticles.Verify(x => x.UpdateAsync(It.Is<KnowledgeBaseArticle>(a => a.HelpfulCount == 11)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectReview_ResetsStatus()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.PendingReview,
            Version = 1,
            ReviewRequired = true
        };
        var version = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 2,
            Status = KbVersionStatusEnum.Pending
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { version }
        );
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;
        var kbVersions = resultExtended.kbVersions;

        var handler = new RejectReviewCommandHandler(uow.Object);
        var command = new RejectReviewCommand { ArticleId = articleId, Reason = "Needs edit" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.Status.Should().Be(KbArticleStatusEnum.Published); // Version > 0 -> Published
        article.ReviewRequired.Should().BeFalse();
        article.ManagerRejectReason.Should().Be("Needs edit");
        version.Status.Should().Be(KbVersionStatusEnum.Rejected);
        version.ManagerRejectReason.Should().Be("Needs edit");
        kbArticles.Verify(x => x.UpdateAsync(article), Times.Once);
        kbVersions.Verify(x => x.UpdateAsync(version), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RollbackCommand_RestoresContent()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "New Title",
            Version = 2
        };
        var oldVersion = new KbArticleVersion
        {
            Id = versionId,
            ArticleId = articleId,
            Title = "Old Title",
            Content = JsonDocument.Parse("\"Symptoms. Steps. Solution.\""),
            MajorVersion = 1,
            MinorVersion = 0
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { oldVersion }
        );
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;
        var kbVersions = resultExtended.kbVersions;

        var handler = new RollbackKbArticleCommandHandler(uow.Object);
        var command = new RollbackKbArticleCommand { ArticleId = articleId, ToVersionId = versionId, CurrentUserId = Guid.NewGuid() };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.Title.Should().Be("Old Title");
        article.Version.Should().Be(3);
        article.Status.Should().Be(KbArticleStatusEnum.Published);
        kbArticles.Verify(x => x.UpdateAsync(article), Times.Once);
        kbVersions.Verify(x => x.AddAsync(It.Is<KbArticleVersion>(v => v.MajorVersion == 3 && v.Title == "Old Title")), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveReview_OnTemplate_Returns400()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            IsTemplate = true,
            Status = KbArticleStatusEnum.Draft
        };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var handler = new ApproveReviewCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new ApproveReviewCommand { ArticleId = articleId }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_RejectReview_OnTemplate_Returns400()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            IsTemplate = true,
            Status = KbArticleStatusEnum.Draft
        };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var handler = new RejectReviewCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new RejectReviewCommand { ArticleId = articleId, Reason = "test" }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_PublishTemplate_ByAdmin_Publishes_And_ApprovesVersion()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            IsTemplate = true,
            Status = KbArticleStatusEnum.Draft,
            Version = 0
        };
        var pendingVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { pendingVersion });
        var uow = resultExtended.uow;
        var kbVersions = resultExtended.kbVersions;

        var handler = new PublishKbArticleCommandHandler(uow.Object);

        // Act
        var result = await handler.Handle(new PublishKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserRole = "Admin"
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(KbArticleStatusEnum.Published);
        article.Status.Should().Be(KbArticleStatusEnum.Published);
        pendingVersion.Status.Should().Be(KbVersionStatusEnum.Approved);
        kbVersions.Verify(x => x.UpdateAsync(It.Is<KbArticleVersion>(v => v.Status == KbVersionStatusEnum.Approved)), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_PublishTemplate_ByManager_Returns403()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            IsTemplate = true,
            Status = KbArticleStatusEnum.Draft
        };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var handler = new PublishKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new PublishKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserRole = "Manager"
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ArchiveTemplate_ByManager_Returns403()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, IsTemplate = true, Status = KbArticleStatusEnum.Published };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var handler = new ArchiveKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new ArchiveKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserRole = "Manager"
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ArchiveTemplate_ByAdmin_Archives()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, IsTemplate = true, Status = KbArticleStatusEnum.Published };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var handler = new ArchiveKbArticleCommandHandler(uow.Object);

        // Act
        var result = await handler.Handle(new ArchiveKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserRole = "Admin"
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.Status.Should().Be(KbArticleStatusEnum.Archived);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RollbackTemplate_ByNonAdmin_Returns403()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = articleId, IsTemplate = true };
        var version = new KbArticleVersion { Id = versionId, ArticleId = articleId };
        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { version });
        var handler = new RollbackKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new RollbackKbArticleCommand
        {
            ArticleId = articleId,
            ToVersionId = versionId,
            CurrentUserId = Guid.NewGuid(),
            CurrentUserRole = "Manager"
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_UpdateCommand_NotFound_Returns404()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var resultExtended = MockTicketUnitOfWork.BuildExtended();
        var uow = resultExtended.uow;

        var handler = new UpdateKbArticleCommandHandler(uow.Object);
        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserId = Guid.NewGuid(),
            CurrentUserRole = "Staff",
            Title = "Updated Title",
            Content = "symptoms. steps. solution."
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_UpdateCommand_Unauthorized_Returns403()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            CreatedByUserId = Guid.NewGuid(), // different user
            IsDeleted = false
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new UpdateKbArticleCommandHandler(uow.Object);
        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserId = Guid.NewGuid(), // different user
            CurrentUserRole = "Customer", // not staff/manager/admin
            Title = "Updated Title",
            Content = "symptoms. steps. solution."
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_UpdateCommand_Success_CreatesPendingVersionAndUpdatesArticle()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var editorId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            CreatedByUserId = creatorId,
            Version = 1,
            IsDeleted = false,
            Status = KbArticleStatusEnum.Published
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;
        var kbVersions = resultExtended.kbVersions;

        var handler = new UpdateKbArticleCommandHandler(uow.Object);
        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            // Người sửa là Staff KHÁC, không phải người tạo bài.
            //
            // Từ commit 0e68b2a6 ("direct KB update for owner and manager without re-approval"),
            // chủ bài viết và Manager/Admin sửa thẳng qua `HandleDirectUpdate` — nhánh đó đặt
            // `ReviewRequired = false` và sinh bản ghi phiên bản *.0 đã duyệt. Test này mô tả
            // nhánh CHỜ DUYỆT (đúng như tên nó và như khối Verify phiên bản 2.1 phía dưới), nên
            // người sửa phải là người ngoài. Trước đây nó truyền chính `creatorId` — commit trên
            // cập nhật KbApiTests nhưng bỏ sót test này, và nó đỏ ngay khi nhánh rebase lên dev.
            CurrentUserId = editorId,
            CurrentUserRole = "Staff",
            Title = "Updated Title",
            Content = "Updated Symptoms. Updated Steps. Updated Solution.",
            ChangeDescription = "Minor updates"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        article.ReviewRequired.Should().BeTrue();
        article.Status.Should().Be(KbArticleStatusEnum.PendingReview);
        // Người chờ duyệt là người VỪA SỬA, không phải người tạo bài.
        article.PendingReviewBy.Should().Be(editorId);

        kbArticles.Verify(x => x.UpdateAsync(article), Times.Once);
        kbVersions.Verify(x => x.AddAsync(It.Is<KbArticleVersion>(v =>
            v.ArticleId == articleId &&
            v.MajorVersion == 2 &&
            v.MinorVersion == 1 &&
            v.Title == "Updated Title" &&
            v.Content.RootElement.GetString() == "Updated Symptoms. Updated Steps. Updated Solution." &&
            v.Status == KbVersionStatusEnum.Pending
        )), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Trạng thái ngay sau khi tạo bài viết: <c>article.Version = 0</c> nhưng đã có sẵn bản 1.0 Pending.
    /// Direct update tính <c>nextMajor = Version + 1 = 1</c> nên rơi trúng ô 1.0 đã bị chiếm — trước đây
    /// <c>AddAsync</c> thẳng làm Postgres nổ 23505 trên
    /// <c>IX_kb_article_versions_article_id_major_version_minor_version</c> và API trả 500.
    /// </summary>
    [Fact]
    public async Task Handle_UpdateCommand_DirectUpdateOnNeverPublishedArticle_ReusesExistingVersionRow()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            CreatedByUserId = ownerId,
            Version = 0,
            Status = KbArticleStatusEnum.PendingReview,
            ReviewRequired = true,
            PendingReviewBy = ownerId
        };
        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = "Khởi tạo",
            Content = JsonDocument.Parse("\"init\"")
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { initialVersion });
        var uow = resultExtended.uow;
        var kbVersions = resultExtended.kbVersions;

        var handler = new UpdateKbArticleCommandHandler(uow.Object);
        var command = new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserId = ownerId,
            CurrentUserRole = "Admin",
            Title = "Updated Title",
            Content = "Updated content.",
            ChangeDescription = "aaaaa"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        article.Version.Should().Be(1);
        article.Title.Should().Be("Updated Title");

        // Ghi đè lên row 1.0 có sẵn, KHÔNG chèn row mới.
        kbVersions.Verify(x => x.AddAsync(It.IsAny<KbArticleVersion>()), Times.Never);
        kbVersions.Verify(x => x.UpdateAsync(initialVersion), Times.Once);
        initialVersion.Status.Should().Be(KbVersionStatusEnum.Approved);
        initialVersion.Title.Should().Be("Updated Title");
    }

    /// <summary>
    /// Bản Staff còn chờ duyệt ở cùng major phải bị đánh Rejected khi có cập nhật trực tiếp, nếu không
    /// lần duyệt sau sẽ ghi đè ngược lại nội dung vừa cập nhật.
    /// </summary>
    [Fact]
    public async Task Handle_UpdateCommand_DirectUpdate_RejectsSiblingPendingVersions()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            CreatedByUserId = ownerId,
            Version = 0,
            Status = KbArticleStatusEnum.PendingReview
        };
        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Content = JsonDocument.Parse("\"init\"")
        };
        var staffDraft = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 1,
            Status = KbVersionStatusEnum.Pending,
            Content = JsonDocument.Parse("\"staff edit\"")
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { initialVersion, staffDraft });

        var handler = new UpdateKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserId = Guid.NewGuid(),
            CurrentUserRole = "Manager",
            Title = "Manager Title",
            Content = "Manager content."
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        initialVersion.Status.Should().Be(KbVersionStatusEnum.Approved);
        staffDraft.Status.Should().Be(KbVersionStatusEnum.Rejected);
        staffDraft.ManagerRejectReason.Should().NotBeNullOrEmpty();
    }

    /// <summary>Rollback trên bài viết chưa từng publish cũng rơi trúng ô 1.0 đã bị chiếm.</summary>
    [Fact]
    public async Task Handle_RollbackCommand_OnNeverPublishedArticle_ReusesExistingVersionRow()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "Current",
            Version = 0
        };
        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = "Old Title",
            Content = JsonDocument.Parse("\"old\"")
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { initialVersion });
        var kbVersions = resultExtended.kbVersions;

        var handler = new RollbackKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new RollbackKbArticleCommand
        {
            ArticleId = articleId,
            ToVersionId = initialVersion.Id,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        article.Version.Should().Be(1);
        article.Title.Should().Be("Old Title");
        kbVersions.Verify(x => x.AddAsync(It.IsAny<KbArticleVersion>()), Times.Never);
        kbVersions.Verify(x => x.UpdateAsync(initialVersion), Times.Once);
        initialVersion.Status.Should().Be(KbVersionStatusEnum.Approved);
    }

    /// <summary>
    /// Row đã soft-delete VẪN giữ chỗ vì unique index không lọc <c>is_deleted</c>. Tra cứu ô phải bỏ qua
    /// điều kiện IsDeleted, nếu không lại INSERT trúng ô đã có và nổ 23505.
    /// </summary>
    [Fact]
    public async Task Handle_UpdateCommand_DirectUpdate_RevivesSoftDeletedVersionRow()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            CreatedByUserId = ownerId,
            Version = 0,
            Status = KbArticleStatusEnum.Draft
        };
        var deletedVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Rejected,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            Content = JsonDocument.Parse("\"deleted\"")
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbSeed: new[] { article },
            kbVersionSeed: new[] { deletedVersion });
        var kbVersions = resultExtended.kbVersions;

        var handler = new UpdateKbArticleCommandHandler(resultExtended.uow.Object);

        // Act
        var result = await handler.Handle(new UpdateKbArticleCommand
        {
            ArticleId = articleId,
            CurrentUserId = ownerId,
            CurrentUserRole = "Admin",
            Title = "Revived",
            Content = "Revived content."
        }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        kbVersions.Verify(x => x.AddAsync(It.IsAny<KbArticleVersion>()), Times.Never);
        deletedVersion.IsDeleted.Should().BeFalse();
        deletedVersion.DeletedAt.Should().BeNull();
        deletedVersion.Status.Should().Be(KbVersionStatusEnum.Approved);
        deletedVersion.Title.Should().Be("Revived");
    }
}
