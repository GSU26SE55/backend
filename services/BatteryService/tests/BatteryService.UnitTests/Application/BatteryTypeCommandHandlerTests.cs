using System.Collections;
using System.Linq.Expressions;
using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.CQRS.Handler.BatteryType;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

public class BatteryTypeCommandHandlerTests
{
    [Fact]
    public async Task CreateBatteryType_NewName_PersistsAndReturnsCreated()
    {
        BatteryType? captured = null;
        var (unitOfWork, batteryTypes) = BuildUnitOfWork();
        batteryTypes
            .Setup(repository => repository.AddAsync(It.IsAny<BatteryType>()))
            .Callback<BatteryType>(entity => captured = entity)
            .Returns(Task.CompletedTask);

        var handler = new CreateBatteryTypeCommandHandler(unitOfWork.Object);

        var result = await handler.Handle(new CreateBatteryTypeCommand
        {
            Name = "LiFePO4 12V 100Ah",
            Manufacturer = "SolarCo",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Name.Should().Be("LiFePO4 12V 100Ah");
        captured.Should().NotBeNull();
        batteryTypes.Verify(repository => repository.AddAsync(It.IsAny<BatteryType>()), Times.Once);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBatteryType_DuplicateName_ReturnsConflict()
    {
        var existing = new BatteryType
        {
            Id = Guid.NewGuid(),
            Name = "LiFePO4 12V 100Ah",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000
        };

        var (unitOfWork, batteryTypes) = BuildUnitOfWork([existing]);
        var handler = new CreateBatteryTypeCommandHandler(unitOfWork.Object);

        var result = await handler.Handle(new CreateBatteryTypeCommand
        {
            Name = "lifepo4 12v 100ah",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        batteryTypes.Verify(repository => repository.AddAsync(It.IsAny<BatteryType>()), Times.Never);
        unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IBatteryUnitOfWork> unitOfWork, Mock<IGenericRepository<BatteryType>> batteryTypes)
        BuildUnitOfWork(IEnumerable<BatteryType>? seed = null)
    {
        var batteryTypes = new Mock<IGenericRepository<BatteryType>>();
        batteryTypes
            .Setup(repository => repository.GetAllAsync())
            .Returns(new TestAsyncEnumerable<BatteryType>(seed ?? Array.Empty<BatteryType>()));

        var unitOfWork = new Mock<IBatteryUnitOfWork>();
        unitOfWork.SetupGet(work => work.BatteryTypes).Returns(batteryTypes.Object);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return (unitOfWork, batteryTypes);
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
