using FluentAssertions;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Command.MaintenanceLogs;
using TicketService.Application.CQRS.Command.Sagas;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

public class TicketCommandValidationTests
{
    #region Basic & Triage & Assign Commands
    [Fact]
    public async Task TicketTriageCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketTriageCommand
        {
            TicketId = Guid.Empty,
            Impact = (ImpactScopeEnum)99,
            Urgency = (UrgencyLevelEnum)99
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Impact");
        result.ListErrors.Should().Contain(x => x.Field == "Urgency");
    }

    [Fact]
    public async Task TicketTriageCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketTriageCommand
        {
            TicketId = Guid.NewGuid(),
            Impact = (ImpactScopeEnum)1,
            Urgency = (UrgencyLevelEnum)1
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketAssignCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketAssignCommand { TicketId = Guid.Empty, PrimaryHandlerStaffId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "PrimaryHandlerStaffId");
    }

    [Fact]
    public async Task TicketAssignCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketAssignCommand { TicketId = Guid.NewGuid(), PrimaryHandlerStaffId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketRejectCommand_EmptyReason_ReturnsErrors()
    {
        var command = new TicketRejectCommand { TicketId = Guid.NewGuid(), Reason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketRejectCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketRejectCommand { TicketId = Guid.NewGuid(), Reason = "Valid reason" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketTriageRejectCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketTriageRejectCommand { TicketId = Guid.Empty, Reason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketTriageRejectCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketTriageRejectCommand { TicketId = Guid.NewGuid(), Reason = "Valid reason" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region Lifecycle Commands
    [Fact]
    public async Task TicketCreateCommand_EmptyData_ReturnsErrors()
    {
        var command = new TicketCreateCommand { Title = "", Description = "", CustomerId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Description");
        result.ListErrors.Should().Contain(x => x.Field == "CustomerId");
        result.ListErrors.Should().Contain(x => x.Field == "IncidentDetectedAt");
    }

    [Fact]
    public async Task TicketCreateCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketCreateCommand { Title = "Title", Description = "Desc", CustomerId = Guid.NewGuid(), BatteryAssetIds = new List<Guid> { Guid.NewGuid() }, IncidentDetectedAt = DateTime.UtcNow.AddHours(-1) };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketCreateCommand_DuplicateAttachmentFileId_ReturnsSuccess()
    {
        var fileId = Guid.NewGuid();
        var command = new TicketCreateCommand
        {
            Title = "Title",
            Description = "Desc",
            CustomerId = Guid.NewGuid(),
            BatteryAssetIds = new List<Guid> { Guid.NewGuid() },
            IncidentDetectedAt = DateTime.UtcNow.AddHours(-1),
            Attachments = new List<TicketAttachmentInput>
            {
                new(fileId, "photo.jpg", "image/jpeg", 1024, "https://files.example/photo.jpg"),
                new(fileId, "photo.jpg", "image/jpeg", 1024, "https://files.example/photo.jpg")
            }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketCreateCommand_MissingIncidentDetectedAt_ReturnsError()
    {
        var command = new TicketCreateCommand { Title = "Title", Description = "Desc", CustomerId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "IncidentDetectedAt");
    }

    [Fact]
    public async Task TicketCreateCommand_IncidentDetectedAtInFuture_ReturnsError()
    {
        var command = new TicketCreateCommand { Title = "Title", Description = "Desc", CustomerId = Guid.NewGuid(), BatteryAssetIds = new List<Guid> { Guid.NewGuid() }, IncidentDetectedAt = DateTime.UtcNow.AddHours(1) };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "IncidentDetectedAt");
    }

    [Fact]
    public async Task TicketResolveCommand_EmptySummary_ReturnsErrors()
    {
        var command = new TicketResolveCommand { TicketId = Guid.Empty, ResolutionSummary = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "ResolutionSummary");
    }

    [Fact]
    public async Task TicketResolveCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketResolveCommand { TicketId = Guid.NewGuid(), ResolutionSummary = "Valid summary" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketHoldCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketHoldCommand { TicketId = Guid.Empty, Reason = (PauseReasonEnum)99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketHoldCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketHoldCommand { TicketId = Guid.NewGuid(), Reason = (PauseReasonEnum)1 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketResumeCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketResumeCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketResumeCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketResumeCommand { TicketId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketStartCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketStartCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketStartCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketStartCommand { TicketId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketApproveCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketApproveCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketApproveCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketApproveCommand { TicketId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketReassignCommand_EmptyIds_ReturnsErrors()
    {
        var command = new TicketReassignCommand { TicketId = Guid.Empty, NewPrimaryHandlerStaffId = Guid.Empty, Reason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "NewPrimaryHandlerStaffId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketReassignCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketReassignCommand { TicketId = Guid.NewGuid(), NewPrimaryHandlerStaffId = Guid.NewGuid(), Reason = "Valid reassign reason" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketEscalateForceCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketEscalateForceCommand { TicketId = Guid.Empty, Reason = (EscalationReasonEnum)99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketEscalateForceCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketEscalateForceCommand { TicketId = Guid.NewGuid(), Reason = (EscalationReasonEnum)1 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketEscalateRequestCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketEscalateRequestCommand { TicketId = Guid.Empty, Reason = (EscalationReasonEnum)99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketEscalateRequestCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketEscalateRequestCommand { TicketId = Guid.NewGuid(), Reason = (EscalationReasonEnum)1 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketAutoCreateFromAlertCommand_EmptyData_ReturnsErrors()
    {
        var command = new TicketAutoCreateFromAlertCommand { OriginAlertId = Guid.Empty, AnomalyCategory = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "OriginAlertId");
        result.ListErrors.Should().Contain(x => x.Field == "AnomalyCategory");
    }

    [Fact]
    public async Task TicketAutoCreateFromAlertCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketAutoCreateFromAlertCommand { OriginAlertId = Guid.NewGuid(), AnomalyCategory = "Valid category" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketRateCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketRateCommand { TicketId = Guid.Empty, Rating = 99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Rating");
    }

    [Fact]
    public async Task TicketRateCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketRateCommand { TicketId = Guid.NewGuid(), Rating = 5 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task TicketReopenCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketReopenCommand { TicketId = Guid.Empty, ReopenReason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "ReopenReason");
    }

    [Fact]
    public async Task TicketReopenCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketReopenCommand { TicketId = Guid.NewGuid(), ReopenReason = "Need check again" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region CommentAdd Commands
    [Fact]
    public async Task ChatAddCommand_InvalidData_ReturnsErrors()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.Empty,
            UserId = Guid.Empty,
            Body = "",
            Attachments = new List<ChatAttachmentInput>
            {
                new(Guid.Empty, "", "", -1)
            }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "UserId");
        result.ListErrors.Should().Contain(x => x.Field == "Body");
        result.ListErrors.Should().Contain(x => x.Field == "Attachments[0].FileId");
        result.ListErrors.Should().Contain(x => x.Field == "Attachments[0].FileName");
        result.ListErrors.Should().Contain(x => x.Field == "Attachments[0].ContentType");
    }

    [Fact]
    public async Task ChatAddCommand_ValidData_ReturnsSuccess()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Body = "Valid Body comment",
            Attachments = new List<ChatAttachmentInput>
            {
                new(Guid.NewGuid(), "test.jpg", "image/jpeg", 2048)
            }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region KnowledgeBase Commands
    [Fact]
    public async Task ApproveReviewCommand_InvalidData_ReturnsErrors()
    {
        var command = new ApproveReviewCommand { ArticleId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
    }

    [Fact]
    public async Task ApproveReviewCommand_ValidData_ReturnsSuccess()
    {
        var command = new ApproveReviewCommand { ArticleId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new ArchiveKbArticleCommand { ArticleId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
    }

    [Fact]
    public async Task ArchiveKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new ArchiveKbArticleCommand { ArticleId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new DeleteKbArticleCommand { ArticleId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
    }

    [Fact]
    public async Task DeleteKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new DeleteKbArticleCommand { ArticleId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkHelpfulCommand_InvalidData_ReturnsErrors()
    {
        var command = new MarkHelpfulCommand { ArticleId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
    }

    [Fact]
    public async Task MarkHelpfulCommand_ValidData_ReturnsSuccess()
    {
        var command = new MarkHelpfulCommand { ArticleId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new PublishKbArticleCommand { ArticleId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
    }

    [Fact]
    public async Task PublishKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new PublishKbArticleCommand { ArticleId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectReviewCommand_InvalidData_ReturnsErrors()
    {
        var command = new RejectReviewCommand { ArticleId = Guid.Empty, Reason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task RejectReviewCommand_ValidData_ReturnsSuccess()
    {
        var command = new RejectReviewCommand { ArticleId = Guid.NewGuid(), Reason = "Reason for reject" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task RollbackKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new RollbackKbArticleCommand { ArticleId = Guid.Empty, ToVersionId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
        result.ListErrors.Should().Contain(x => x.Field == "ToVersionId");
    }

    [Fact]
    public async Task RollbackKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new RollbackKbArticleCommand { ArticleId = Guid.NewGuid(), ToVersionId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new CreateKbArticleCommand
        {
            Title = "",
            Content = "",
            Category = (TicketCategoryEnum)99,
            Tags = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" } // 11 tags
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Content");
        result.ListErrors.Should().Contain(x => x.Field == "Category");
        result.ListErrors.Should().Contain(x => x.Field == "Tags");
    }

    [Fact]
    public async Task CreateKbArticleCommand_TitleTooLong_ReturnsErrors()
    {
        var command = new CreateKbArticleCommand
        {
            Title = new string('a', 201),
            Content = "valid content",
            Category = (TicketCategoryEnum)1,
            Tags = new List<string> { new string('x', 51) }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Tags");
    }

    [Fact]
    public async Task CreateKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new CreateKbArticleCommand
        {
            Title = "Valid Title",
            Content = "Valid content for KB article.",
            Category = (TicketCategoryEnum)1,
            Tags = new List<string> { "valid" }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateKbArticleCommand_InvalidData_ReturnsErrors()
    {
        var command = new UpdateKbArticleCommand
        {
            ArticleId = Guid.Empty,
            Title = "",
            Content = "",
            Category = (TicketCategoryEnum)99,
            Tags = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ArticleId");
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Content");
        result.ListErrors.Should().Contain(x => x.Field == "Category");
        result.ListErrors.Should().Contain(x => x.Field == "Tags");
    }

    [Fact]
    public async Task UpdateKbArticleCommand_TitleTooLong_ReturnsErrors()
    {
        var command = new UpdateKbArticleCommand
        {
            ArticleId = Guid.NewGuid(),
            Title = new string('a', 201),
            Content = "valid content",
            Category = (TicketCategoryEnum)1,
            Tags = new List<string> { new string('x', 51) }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Tags");
    }

    [Fact]
    public async Task UpdateKbArticleCommand_ValidData_ReturnsSuccess()
    {
        var command = new UpdateKbArticleCommand
        {
            ArticleId = Guid.NewGuid(),
            Title = "Valid Title",
            Content = "Valid content for KB article.",
            Category = (TicketCategoryEnum)1,
            Tags = new List<string> { "valid" }
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region MaintenanceLog Commands
    [Fact]
    public async Task MaintenanceLogAddCommand_InvalidData_ReturnsErrors()
    {
        var command = new MaintenanceLogAddCommand
        {
            TicketId = Guid.Empty,
            StaffId = Guid.Empty,
            Summary = "",
            DurationMinutes = -5,
            StartedAt = default
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "StaffId");
        result.ListErrors.Should().Contain(x => x.Field == "Summary");
        result.ListErrors.Should().Contain(x => x.Field == "DurationMinutes");
        result.ListErrors.Should().Contain(x => x.Field == "StartedAt");
    }

    [Fact]
    public async Task MaintenanceLogAddCommand_ValidData_ReturnsSuccess()
    {
        var command = new MaintenanceLogAddCommand
        {
            TicketId = Guid.NewGuid(),
            StaffId = Guid.NewGuid(),
            Summary = "Valid Summary",
            DurationMinutes = 60,
            StartedAt = DateTime.UtcNow
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MaintenanceLogUpdateCommand_InvalidData_ReturnsErrors()
    {
        var command = new MaintenanceLogUpdateCommand
        {
            LogId = Guid.Empty,
            StaffId = Guid.Empty,
            Summary = "",
            DurationMinutes = -1
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "LogId");
        result.ListErrors.Should().Contain(x => x.Field == "StaffId");
        result.ListErrors.Should().Contain(x => x.Field == "Summary");
        result.ListErrors.Should().Contain(x => x.Field == "DurationMinutes");
    }

    [Fact]
    public async Task MaintenanceLogUpdateCommand_ValidData_ReturnsSuccess()
    {
        var command = new MaintenanceLogUpdateCommand
        {
            LogId = Guid.NewGuid(),
            StaffId = Guid.NewGuid(),
            Summary = "New Summary",
            DurationMinutes = 30
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region Saga Commands
    [Fact]
    public async Task ReprocessSagaCommand_InvalidData_ReturnsErrors()
    {
        var command = new ReprocessSagaCommand
        {
            AlertId = Guid.Empty,
            IdempotencyKey = "",
            PerformedBy = Guid.Empty
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == nameof(ReprocessSagaCommand.AlertId));
        result.ListErrors.Should().Contain(x => x.Field == nameof(ReprocessSagaCommand.IdempotencyKey));
        result.ListErrors.Should().Contain(x => x.Field == nameof(ReprocessSagaCommand.PerformedBy));
    }

    [Fact]
    public async Task ReprocessSagaCommand_ValidData_ReturnsSuccess()
    {
        var command = new ReprocessSagaCommand
        {
            AlertId = Guid.NewGuid(),
            IdempotencyKey = "key-123",
            PerformedBy = Guid.NewGuid()
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion

    #region Incident & KB Reference Commands
    [Fact]
    public async Task TicketDeclareIncidentCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketDeclareIncidentCommand
        {
            TicketId = Guid.Empty,
            UserId = Guid.Empty,
            IncidentDescription = ""
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "UserId");
        result.ListErrors.Should().Contain(x => x.Field == "IncidentDescription");
    }

    [Fact]
    public async Task TicketDeclareIncidentCommand_ValidData_ReturnsSuccess()
    {
        var command = new TicketDeclareIncidentCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            IncidentDescription = "Incident occurs"
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task AddTicketKbReferenceCommand_InvalidData_ReturnsErrors()
    {
        var command = new AddTicketKbReferenceCommand
        {
            TicketId = Guid.Empty,
            KbArticleId = Guid.Empty,
            ReferenceType = (KbReferenceTypeEnum)99
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "KbArticleId");
        result.ListErrors.Should().Contain(x => x.Field == "ReferenceType");
    }

    [Fact]
    public async Task AddTicketKbReferenceCommand_ValidData_ReturnsSuccess()
    {
        var command = new AddTicketKbReferenceCommand
        {
            TicketId = Guid.NewGuid(),
            KbArticleId = Guid.NewGuid(),
            ReferenceType = (KbReferenceTypeEnum)1
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveTicketKbReferenceCommand_InvalidData_ReturnsErrors()
    {
        var command = new RemoveTicketKbReferenceCommand { ReferenceId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "ReferenceId");
    }

    [Fact]
    public async Task RemoveTicketKbReferenceCommand_ValidData_ReturnsSuccess()
    {
        var command = new RemoveTicketKbReferenceCommand { ReferenceId = Guid.NewGuid() };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }
    #endregion
}
