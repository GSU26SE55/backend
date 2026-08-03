using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TemplateEntity = NotificationService.Domain.Entities.NotificationTemplate;

namespace NotificationService.UnitTests.Handlers.NotificationTemplate;

/// <summary>
/// Bộ khung dựng sẵn cho các handler của notification template.
///
/// <para><see cref="Templates"/> là danh sách CÓ THỂ THAY ĐỔI và mọi lần gọi <c>GetAllAsync()</c>
/// đều dựng lại queryable từ nó — nhờ vậy test quan sát được tác dụng của Add/Update/Delete, thay vì
/// chụp ảnh dữ liệu một lần lúc khởi tạo rồi không bao giờ thấy thay đổi.</para>
/// </summary>
internal sealed class TemplateHandlerHarness
{
    public List<TemplateEntity> Templates { get; } = new();
    public Mock<INotificationUnitOfWork> Uow { get; } = new();
    public Mock<IGenericRepository<TemplateEntity>> Repo { get; } = new();
    public Mock<INotificationAuditWriter> Audit { get; } = new();
    public Mock<IPublishEndpoint> Publisher { get; } = new();
    public Mock<ICacheService> Cache { get; } = new();

    /// <summary>Renderer THẬT — kiểm cú pháp Handlebars phải là hành vi thật, mock đi thì test vô nghĩa.</summary>
    public ITemplateRenderer Renderer { get; } = new HandlebarsTemplateRenderer();

    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public TemplateHandlerHarness(params TemplateEntity[] seed)
    {
        Templates.AddRange(seed);

        Repo.Setup(r => r.GetAllAsync()).Returns(() => Templates.AsQueryable().BuildMock());
        Repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(() => Templates.AsQueryable().BuildMock());
        Repo.Setup(r => r.AddAsync(It.IsAny<TemplateEntity>()))
            .Callback<TemplateEntity>(Templates.Add)
            .Returns(Task.CompletedTask);
        Repo.Setup(r => r.UpdateAsync(It.IsAny<TemplateEntity>()));
        // Soft delete do AuditableEntityInterceptor làm ở tầng EF; ở unit test mô phỏng lại tác dụng
        // để test kiểm được "sau khi xoá thì bản ghi biến khỏi truy vấn".
        Repo.Setup(r => r.DeleteAsync(It.IsAny<TemplateEntity>()))
            .Callback<TemplateEntity>(t => t.IsDeleted = true);

        var accounts = new Mock<IGenericRepository<NotificationService.Domain.Entities.AccountReadModel>>();
        accounts.Setup(a => a.GetAllAsync()).Returns(Array.Empty<NotificationService.Domain.Entities.AccountReadModel>().AsQueryable().BuildMock());
        accounts.Setup(a => a.GetAllAsync(It.IsAny<bool>())).Returns(Array.Empty<NotificationService.Domain.Entities.AccountReadModel>().AsQueryable().BuildMock());

        Uow.SetupGet(u => u.NotificationTemplates).Returns(Repo.Object);
        Uow.SetupGet(u => u.Accounts).Returns(accounts.Object);
        Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        Uow.Setup(u => u.CommitTransactionAsync()).Callback(() => Committed = true).Returns(Task.CompletedTask);
        Uow.Setup(u => u.RollbackTransactionAsync()).Callback(() => RolledBack = true).Returns(Task.CompletedTask);

        Cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
    }

    public static TemplateEntity Template(
        NotificationTypeEnum type = NotificationTypeEnum.TicketCreated,
        NotificationChannelEnum channel = NotificationChannelEnum.Email,
        int version = 1,
        bool isActive = true,
        bool isDeleted = false,
        string title = "Ticket {{code}}",
        string body = "Ticket ưu tiên {{priority}}.") => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Channel = channel,
            Version = version,
            IsActive = isActive,
            IsDeleted = isDeleted,
            TitleTemplate = title,
            BodyTemplate = body,
            CreatedAt = DateTime.UtcNow,
        };

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;
}
