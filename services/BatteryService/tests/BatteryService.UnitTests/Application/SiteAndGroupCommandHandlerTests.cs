using System.Collections;
using System.Linq.Expressions;
using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.CQRS.Handler.BatteryAsset;
using BatteryService.Application.CQRS.Handler.BatteryGroup;
using BatteryService.Application.CQRS.Handler.Site;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

public class SiteAndGroupCommandHandlerTests
{
    [Fact]
    public async Task CreateSite_DuplicateNameForCustomer_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var existing = new Site
        {
            Id = Guid.NewGuid(),
            Name = "Solar Farm Long An",
            CustomerId = customerId,
            InstallDate = DateTime.UtcNow.AddDays(-1)
        };
        var customer = ActiveCustomer(customerId);

        var (unitOfWork, repos) = BuildUnitOfWork(sites: [existing], customerAccounts: [customer]);
        var handler = new CreateSiteCommandHandler(unitOfWork.Object);

        var result = await handler.Handle(new CreateSiteCommand
        {
            Name = "solar farm long an",
            CustomerId = customerId,
            InstallDate = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        repos.Sites.Verify(repository => repository.AddAsync(It.IsAny<Site>()), Times.Never);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBatteryGroup_ValidRequest_PersistsGroup()
    {
        BatteryGroup? captured = null;
        var site = new Site
        {
            Id = Guid.NewGuid(),
            Name = "Solar Farm Long An",
            CustomerId = Guid.NewGuid(),
            InstallDate = DateTime.UtcNow.AddDays(-1)
        };
        var batteryType = new BatteryType
        {
            Id = Guid.NewGuid(),
            Name = "LiFePO4 12V 100Ah",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000
        };

        var (unitOfWork, repos) = BuildUnitOfWork(sites: [site], batteryTypes: [batteryType]);
        repos.BatteryGroups
            .Setup(repository => repository.AddAsync(It.IsAny<BatteryGroup>()))
            .Callback<BatteryGroup>(entity => captured = entity)
            .Returns(Task.CompletedTask);

        var handler = new CreateBatteryGroupCommandHandler(unitOfWork.Object);

        var result = await handler.Handle(new CreateBatteryGroupCommand
        {
            SiteId = site.Id,
            Name = "Block A",
            BatteryTypeId = batteryType.Id
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        captured.Should().NotBeNull();
        captured!.SiteId.Should().Be(site.Id);
        captured.BatteryTypeId.Should().Be(batteryType.Id);
        captured.BatteryCount.Should().Be(0);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBatteryAsset_GroupTypeMismatch_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var requestedTypeId = Guid.NewGuid();
        var groupTypeId = Guid.NewGuid();
        var site = new Site
        {
            Id = Guid.NewGuid(),
            Name = "Solar Farm Long An",
            CustomerId = customerId,
            InstallDate = DateTime.UtcNow.AddDays(-1)
        };
        var requestedType = new BatteryType
        {
            Id = requestedTypeId,
            Name = "LiFePO4 12V 100Ah",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 3000
        };
        var group = new BatteryGroup
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            Site = site,
            Name = "Block A",
            BatteryTypeId = groupTypeId
        };
        var customer = ActiveCustomer(customerId);

        var (unitOfWork, repos) = BuildUnitOfWork(
            batteryTypes: [requestedType],
            sites: [site],
            batteryGroups: [group],
            customerAccounts: [customer]);
        var handler = new CreateBatteryAssetCommandHandler(unitOfWork.Object);

        var result = await handler.Handle(new CreateBatteryAssetCommand
        {
            SerialNumber = "BAT-2026-001",
            BatteryTypeId = requestedTypeId,
            CustomerId = customerId,
            SiteId = site.Id,
            BatteryGroupId = group.Id,
            InstallDate = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        repos.BatteryAssets.Verify(repository => repository.AddAsync(It.IsAny<BatteryAsset>()), Times.Never);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IBatteryUnitOfWork> unitOfWork, RepositoryMocks repos) BuildUnitOfWork(
        IEnumerable<BatteryType>? batteryTypes = null,
        IEnumerable<BatteryAsset>? batteryAssets = null,
        IEnumerable<Site>? sites = null,
        IEnumerable<BatteryGroup>? batteryGroups = null,
        IEnumerable<CustomerAccount>? customerAccounts = null)
    {
        var repos = new RepositoryMocks
        {
            BatteryTypes = BuildRepository(batteryTypes),
            BatteryAssets = BuildRepository(batteryAssets),
            Sites = BuildRepository(sites),
            BatteryGroups = BuildRepository(batteryGroups),
            CustomerAccounts = BuildRepository(customerAccounts)
        };

        var unitOfWork = new Mock<IBatteryUnitOfWork>();
        unitOfWork.SetupGet(work => work.BatteryTypes).Returns(repos.BatteryTypes.Object);
        unitOfWork.SetupGet(work => work.BatteryAssets).Returns(repos.BatteryAssets.Object);
        unitOfWork.SetupGet(work => work.Sites).Returns(repos.Sites.Object);
        unitOfWork.SetupGet(work => work.BatteryGroups).Returns(repos.BatteryGroups.Object);
        unitOfWork.SetupGet(work => work.CustomerAccounts).Returns(repos.CustomerAccounts.Object);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return (unitOfWork, repos);
    }

    private static Mock<IGenericRepository<T>> BuildRepository<T>(IEnumerable<T>? seed = null) where T : class
    {
        var repository = new Mock<IGenericRepository<T>>();
        repository
            .Setup(item => item.GetAllAsync())
            .Returns(new TestAsyncEnumerable<T>(seed ?? Array.Empty<T>()));
        return repository;
    }

    private sealed class RepositoryMocks
    {
        public Mock<IGenericRepository<BatteryType>> BatteryTypes { get; init; } = null!;
        public Mock<IGenericRepository<BatteryAsset>> BatteryAssets { get; init; } = null!;
        public Mock<IGenericRepository<Site>> Sites { get; init; } = null!;
        public Mock<IGenericRepository<BatteryGroup>> BatteryGroups { get; init; } = null!;
        public Mock<IGenericRepository<CustomerAccount>> CustomerAccounts { get; init; } = null!;
    }

    private static CustomerAccount ActiveCustomer(Guid id)
    {
        return new CustomerAccount
        {
            Id = id,
            Email = "customer@example.com",
            FullName = "Customer",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = DateTime.UtcNow
        };
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner)
        {
            _inner = inner;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new TestAsyncEnumerable<TEntity>(expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new TestAsyncEnumerable<TElement>(expression);
        }

        public object? Execute(Expression expression)
        {
            return _inner.Execute(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return _inner.Execute<TResult>(expression);
        }

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var expectedResultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(IQueryProvider.Execute) &&
                    method.IsGenericMethod &&
                    method.GetParameters().Length == 1)
                .MakeGenericMethod(expectedResultType)
                .Invoke(this, [expression]);

            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(expectedResultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable)
        {
        }

        public TestAsyncEnumerable(Expression expression) : base(expression)
        {
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
        }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public T Current => _inner.Current;

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return ValueTask.FromResult(_inner.MoveNext());
        }
    }
}
