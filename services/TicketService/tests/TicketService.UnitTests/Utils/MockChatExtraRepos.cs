using System.Linq.Expressions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Utils;

/// <summary>
/// Extension setup cho 3 repo mới (#536/#539/#541) trên 1 <see cref="ITicketUnitOfWork"/> mock đã có sẵn
/// (tạo từ <see cref="MockTicketUnitOfWork"/>) — KHÔNG đổi signature <c>BuildExtended</c> để tránh phá vỡ
/// 21 test file đang positional-destructure tuple đó.
/// Dùng <see cref="List{T}"/> mutable làm seed để GetAllAsync() phản ánh thay đổi sau AddAsync/DeleteAsync
/// trong cùng 1 test (mô phỏng interceptor soft-delete của production).
/// </summary>
public static class MockChatExtraRepos
{
    public static Mock<IGenericRepository<TicketChatMention>> SetupMentions(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatMention>? seed = null)
    {
        seed ??= new List<TicketChatMention>();
        var repo = new Mock<IGenericRepository<TicketChatMention>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => seed.FirstOrDefault(x => x.Id == (Guid)id));
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatMention>())).Returns((TicketChatMention m) => { seed.Add(m); return Task.CompletedTask; });
        repo.Setup(r => r.UpdateAsync(It.IsAny<TicketChatMention>())).Callback((TicketChatMention _) => { });
        uow.SetupGet(u => u.TicketChatMentions).Returns(repo.Object);
        return repo;
    }

    public static Mock<IGenericRepository<TicketChatReaction>> SetupReactions(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatReaction>? seed = null)
    {
        seed ??= new List<TicketChatReaction>();
        var repo = new Mock<IGenericRepository<TicketChatReaction>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatReaction>())).Returns((TicketChatReaction r) => { seed.Add(r); return Task.CompletedTask; });
        repo.Setup(r => r.UpdateAsync(It.IsAny<TicketChatReaction>())).Callback((TicketChatReaction _) => { });
        repo.Setup(r => r.DeleteAsync(It.IsAny<TicketChatReaction>())).Callback((TicketChatReaction r) => { r.IsDeleted = true; });
        uow.SetupGet(u => u.TicketChatReactions).Returns(repo.Object);
        return repo;
    }

    public static Mock<IGenericRepository<TicketChatRead>> SetupReads(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatRead>? seed = null)
    {
        seed ??= new List<TicketChatRead>();
        var repo = new Mock<IGenericRepository<TicketChatRead>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatRead>())).Returns((TicketChatRead r) => { seed.Add(r); return Task.CompletedTask; });
        uow.SetupGet(u => u.TicketChatReads).Returns(repo.Object);
        return repo;
    }

    public static Mock<IGenericRepository<TicketChatTranslation>> SetupChatTranslations(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatTranslation>? seed = null)
    {
        seed ??= new List<TicketChatTranslation>();
        var repo = new Mock<IGenericRepository<TicketChatTranslation>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatTranslation>())).Returns((TicketChatTranslation t) => { seed.Add(t); return Task.CompletedTask; });
        uow.SetupGet(u => u.TicketChatTranslations).Returns(repo.Object);
        return repo;
    }

    public static Mock<IGenericRepository<TicketChatTranslationUser>> SetupChatTranslationUsers(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatTranslationUser>? seed = null)
    {
        seed ??= new List<TicketChatTranslationUser>();
        var repo = new Mock<IGenericRepository<TicketChatTranslationUser>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatTranslationUser>())).Returns((TicketChatTranslationUser u) => { seed.Add(u); return Task.CompletedTask; });
        uow.SetupGet(u => u.ChatTranslationUsers).Returns(repo.Object);
        return repo;
    }

    public static Mock<IGenericRepository<TicketChatHide>> SetupChatHides(
        this Mock<ITicketUnitOfWork> uow, List<TicketChatHide>? seed = null)
    {
        seed ??= new List<TicketChatHide>();
        var repo = new Mock<IGenericRepository<TicketChatHide>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<TicketChatHide, bool>>>()))
            .ReturnsAsync((Expression<Func<TicketChatHide, bool>> pred) => seed.AsQueryable().Any(pred));
        repo.Setup(r => r.AddAsync(It.IsAny<TicketChatHide>()))
            .Returns((TicketChatHide h) => { seed.Add(h); return Task.CompletedTask; });
        uow.SetupGet(u => u.TicketChatHides).Returns(repo.Object);
        return repo;
    }
}
