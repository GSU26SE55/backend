using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace TicketService.UnitTests.Utils;

public sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncEnumerable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

public sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;
    public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
    public T Current => _inner.Current;
    public ValueTask DisposeAsync() { _inner.Dispose(); return ValueTask.CompletedTask; }
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
}

public sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;
    public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

    public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
    public object? Execute(Expression expression) => _inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var expectedResultType = typeof(TResult).GetGenericArguments()[0];
        var executionResult = typeof(IQueryProvider)
            .GetMethods()
            .Single(method => method.Name == nameof(IQueryProvider.Execute)
                && method.IsGenericMethod
                && method.GetParameters().Length == 1)
            .MakeGenericMethod(expectedResultType)
            .Invoke(this, new object[] { expression });

        return (TResult)typeof(Task)
            .GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(expectedResultType)
            .Invoke(null, new[] { executionResult })!;
    }
}
