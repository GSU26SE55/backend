using FluentAssertions;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class MyMentionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_NoMentions_ReturnsEmptyPage()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupMentions();
        var handler = new MyMentionsQueryHandler(uow.Object);

        var result = await handler.Handle(new MyMentionsQuery
        {
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new List<string> { "Customer" }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }
}
