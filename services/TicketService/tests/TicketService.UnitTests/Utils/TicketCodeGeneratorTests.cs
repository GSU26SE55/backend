using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.Infrastructure.Persistence;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Utils;

public class TicketCodeGeneratorTests
{
    /// <summary>
    /// <see cref="TicketCodeGenerator"/> cần một <see cref="TicketDbContext"/> chỉ để xin advisory
    /// lock của PostgreSQL (chống 2 tiến trình cùng cấp một số ticket). Với provider InMemory, lock
    /// được bỏ qua nhờ guard <c>Database.IsNpgsql()</c> trong generator, nên context ở đây thuần là
    /// chỗ giữ chỗ — dữ liệu ticket vẫn đến từ mock UnitOfWork.
    ///
    /// Mỗi lần gọi tạo một database name riêng: EF InMemory chia sẻ store theo tên, dùng chung tên
    /// sẽ khiến các test rò trạng thái sang nhau.
    /// </summary>
    private static TicketDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseInMemoryDatabase($"ticket-code-generator-{Guid.NewGuid():N}")
            .Options;

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns((string?)null);

        return new TicketDbContext(options, new AuditableEntityInterceptor(currentUser.Object));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsCorrectFormat()
    {
        // Arrange
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        using var db = BuildInMemoryDb();
        var generator = new TicketCodeGenerator(uow.Object, db);

        // Act
        var code = await generator.GenerateAsync();

        // Assert
        code.Should().StartWith("TKT-");
        code.Should().HaveLength(13); // TKT-YYMM-XXXX = 3+1+4+1+4 = 13
    }

    [Fact]
    public async Task GenerateAsync_IncrementsSequence()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var prefix = $"TKT-{now:yyMM}-";
        var existingTicket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = $"{prefix}0005",
            Title = "T",
            Description = "D"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { existingTicket });
        using var db = BuildInMemoryDb();
        var generator = new TicketCodeGenerator(uow.Object, db);

        // Act
        var code = await generator.GenerateAsync();

        // Assert
        code.Should().Be($"{prefix}0006");
    }

    /// <summary>
    /// Chặn hồi quy của chính lỗi vừa sửa: generator KHÔNG được ném khi chạy trên provider
    /// không phải PostgreSQL. Trước khi có guard <c>Database.IsNpgsql()</c>, dòng
    /// <c>ExecuteSqlInterpolatedAsync</c> ném <see cref="InvalidOperationException"/>
    /// ("Relational-specific methods…") ngay trước khi sinh được mã nào.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OnNonPostgresProvider_SkipsAdvisoryLockInsteadOfThrowing()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        using var db = BuildInMemoryDb();
        var generator = new TicketCodeGenerator(uow.Object, db);

        var act = async () => await generator.GenerateAsync();

        await act.Should().NotThrowAsync();
    }
}
