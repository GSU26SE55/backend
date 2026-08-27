using System.Reflection;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.BackgroundJobs;
using AuthService.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Infrastructure.BackgroundJobs;

public class AccountProjectionReconciliationBackgroundServiceTests
{
    [Fact]
    public async Task ReconcileOnce_PublishesAuthoritativeSnapshotForAllAccounts()
    {
        var sender = new Mock<ISender>();
        sender.Setup(item => item.Send(
                It.Is<AccountResyncCommand>(command => command.AccountId == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResyncResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new AccountResyncDto { TotalAccounts = 7 }
            });

        var services = new ServiceCollection();
        services.AddSingleton(sender.Object);
        await using var provider = services.BuildServiceProvider();

        var worker = new AccountProjectionReconciliationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AccountProjectionReconciliationOptions { Enabled = true }),
            NullLogger<AccountProjectionReconciliationBackgroundService>.Instance);

        var method = typeof(AccountProjectionReconciliationBackgroundService)
            .GetMethod("ReconcileOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(worker, new object[] { CancellationToken.None })!;
        await task;

        sender.Verify(item => item.Send(
            It.Is<AccountResyncCommand>(command => command.AccountId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LockoutReconcile_UpdatesAccountAndPublishesProjectionEventInSameSave()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"lockout-reconcile-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(Mock.Of<ICurrentUserService>()));
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "locked@example.com",
            PasswordHash = "x",
            FullName = "Locked Customer",
            Role = role,
            RoleId = role.Id,
            Status = AccountStatusEnum.Locked,
            LockoutEndAt = DateTime.UtcNow.AddMinutes(-1),
            FailedLoginAttempts = 5
        };
        await db.AddRangeAsync(role, account);
        await db.SaveChangesAsync();

        var producer = new Mock<IMessageProducerService>();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(producer.Object);
        await using var provider = services.BuildServiceProvider();
        var worker = new LockoutReconcileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LockoutReconcileBackgroundService>.Instance);

        var method = typeof(LockoutReconcileBackgroundService)
            .GetMethod("ReconcileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(worker, new object[] { CancellationToken.None })!;

        account.Status.Should().Be(AccountStatusEnum.Active);
        account.LockoutEndAt.Should().BeNull();
        account.FailedLoginAttempts.Should().Be(0);
        producer.Verify(item => item.PublishAsync(
            It.Is<AccountStatusChangedEvent>(evt =>
                evt.AccountId == account.Id
                && evt.Role == "Customer"
                && evt.NewStatus == (int)AccountStatusEnum.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
