using FluentAssertions;
using Moq;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Handler.Suggestions;
using TicketService.Application.CQRS.Query.Suggestions;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Suggestions;

public class TicketKbSuggestionsQueryHandlerTests
{
    private readonly Mock<IAiKbSuggestClient> _ai = new();

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Battery overheating",
        Description = "Temperature exceeded threshold",
        Category = TicketCategoryEnum.Overheat,
        Status = TicketStatusEnum.InProgress,
        CustomerId = Guid.NewGuid(),
    };

    private static KnowledgeBaseArticle MakeKb(Guid id, KbArticleStatusEnum status = KbArticleStatusEnum.Published) => new()
    {
        Id = id,
        Code = "KB-2026-0001",
        Title = "Handling battery overheating",
        Category = TicketCategoryEnum.Overheat,
        Status = status,
        Tags = new List<string> { "nhiet" },
        HelpfulCount = 5,
    };

    private static Mock<ITicketCurrentUserService> User(string role, Guid? userId = null)
    {
        var m = new Mock<ITicketCurrentUserService>();
        m.SetupGet(x => x.Role).Returns(role);
        m.SetupGet(x => x.UserId).Returns((userId ?? Guid.NewGuid()).ToString());
        return m;
    }

    private void SetupAiEcho() =>
        _ai.Setup(x => x.SuggestKbAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AiKbCandidate>>(),
                It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiKbSuggestResult(Array.Empty<AiKbSuggestion>(), ""));

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var handler = new TicketKbSuggestionsQueryHandler(uow.Object, _ai.Object, User("Manager").Object);

        var res = await handler.Handle(
            new TicketKbSuggestionsQuery { TicketId = Guid.NewGuid() }, CancellationToken.None);

        res.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ManagerCanViewAnyTicket()
    {
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { MakeTicket(ticketId) },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) });
        SetupAiEcho();

        var res = await new TicketKbSuggestionsQueryHandler(ext.uow.Object, _ai.Object, User("Manager").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_UnassignedStaff_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { MakeTicket(ticketId) },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) });

        var res = await new TicketKbSuggestionsQueryHandler(ext.uow.Object, _ai.Object, User("Staff").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(AssignmentRoleEnum.PrimaryHandler)]
    [InlineData(AssignmentRoleEnum.Supporter)]
    public async Task Handle_AssignedStaffCanView(AssignmentRoleEnum role)
    {
        // Supporter được XEM gợi ý (họ cũng đang sửa chữa); quyền GẮN tài liệu vẫn chỉ
        // dành cho PrimaryHandler ở AddTicketKbReferenceCommandHandler.
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) },
            assignmentSeed: new[]
            {
                new TicketAssignment
                {
                    Id = Guid.NewGuid(), TicketId = ticketId, StaffId = staffId,
                    Role = role, Ticket = ticket,
                },
            });
        SetupAiEcho();

        var res = await new TicketKbSuggestionsQueryHandler(
                ext.uow.Object, _ai.Object, User("Staff", staffId).Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_PreviousPrimaryHandler_Returns403()
    {
        // Đã bị chuyển giao thì không còn quyền xem.
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) },
            assignmentSeed: new[]
            {
                new TicketAssignment
                {
                    Id = Guid.NewGuid(), TicketId = ticketId, StaffId = staffId,
                    Role = AssignmentRoleEnum.PreviousPrimaryHandler, Ticket = ticket,
                },
            });

        var res = await new TicketKbSuggestionsQueryHandler(
                ext.uow.Object, _ai.Object, User("Staff", staffId).Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_CustomerRole_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { MakeTicket(ticketId) },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) });

        var res = await new TicketKbSuggestionsQueryHandler(
                ext.uow.Object, _ai.Object, User("Customer").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_OnlyPublishedArticlesSent()
    {
        // Draft/PendingReview chưa qua duyệt — không đưa cho kỹ thuật viên.
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { MakeTicket(ticketId) },
            kbSeed: new[]
            {
                MakeKb(Guid.NewGuid()),
                MakeKb(Guid.NewGuid(), KbArticleStatusEnum.Draft),
                MakeKb(Guid.NewGuid(), KbArticleStatusEnum.PendingReview),
            });

        IReadOnlyList<AiKbCandidate>? captured = null;
        _ai.Setup(x => x.SuggestKbAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AiKbCandidate>>(),
                It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, IReadOnlyList<AiKbCandidate>, int, IReadOnlyList<string>,
                IReadOnlyList<string>, IReadOnlyList<string>, CancellationToken>(
                (_, _, c, _, _, _, _, _) => captured = c)
            .ReturnsAsync(new AiKbSuggestResult(Array.Empty<AiKbSuggestion>(), ""));

        await new TicketKbSuggestionsQueryHandler(ext.uow.Object, _ai.Object, User("Manager").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        captured.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_AiUnavailable_ReturnsFlagNotError()
    {
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { MakeTicket(ticketId) },
            kbSeed: new[] { MakeKb(Guid.NewGuid()) });

        _ai.Setup(x => x.SuggestKbAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AiKbCandidate>>(),
                It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiKbSuggestResult?)null);

        var res = await new TicketKbSuggestionsQueryHandler(ext.uow.Object, _ai.Object, User("Admin").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.Data!.AiAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoPublishedArticles_ReturnsNote()
    {
        var ticketId = Guid.NewGuid();
        var ext = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { MakeTicket(ticketId) });

        var res = await new TicketKbSuggestionsQueryHandler(ext.uow.Object, _ai.Object, User("Manager").Object)
            .Handle(new TicketKbSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.Data!.Items.Should().BeEmpty();
        res.Data.Note.Should().NotBeNullOrEmpty();
    }
}
