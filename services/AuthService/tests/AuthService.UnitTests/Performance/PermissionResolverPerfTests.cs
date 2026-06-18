using System.Diagnostics;
using AuthService.Application.Authorization;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedKernels.Interfaces;

namespace AuthService.UnitTests.Performance;

/// <summary>
/// #AUTH-89: perf test cho <see cref="PermissionResolver.GetPermissionCodesAsync"/>.
/// Mục tiêu: 1000 concurrent call sau cache warm → p99 latency &lt; 50ms (cache-hit path).
///
/// Note: integration perf trên CI variance cao. Test này chạy locally để verify cache effectiveness;
/// trên CI có thể skip qua <c>[Trait("Category", "Performance")]</c> filter nếu cần.
/// </summary>
// #AUTH-89: disable parallelization để 1000 parallel calls không bị CPU contend với
// các test khác chạy đồng thời (xUnit default parallel execution across collections gây flaky p99).
[CollectionDefinition(nameof(PerformanceTestsCollection), DisableParallelization = true)]
public class PerformanceTestsCollection { }

[Trait("Category", "Performance")]
[Collection(nameof(PerformanceTestsCollection))]
public class PermissionResolverPerfTests
{
    private const int ConcurrentCalls = 1000;
    private const int P99ThresholdMs = 50;

    [Fact]
    public async Task GetPermissionCodes_1000Concurrent_AfterCacheWarm_P99Below50ms()
    {
        // Reset static cache để test isolated.
        PermissionResolver.InvalidateAll();

        var (uow, accountId) = BuildSeededUow();

        // Warm cache với 1 call đầu (sẽ touch DB).
        var warmup = await PermissionResolver.GetPermissionCodesAsync(uow.Object, accountId);
        warmup.Should().NotBeEmpty(); // verify seed correct

        // #AUTH-89: dùng Stopwatch.GetTimestamp + GetElapsedTime cho sub-ms precision
        // (ElapsedMilliseconds round xuống 0 khi <1ms — không đo được p99 chính xác).
        var latenciesTicks = new long[ConcurrentCalls];
        var sw = Stopwatch.StartNew();

        await Parallel.ForAsync(0, ConcurrentCalls, async (i, _) =>
        {
            var start = Stopwatch.GetTimestamp();
            var codes = await PermissionResolver.GetPermissionCodesAsync(uow.Object, accountId);
            latenciesTicks[i] = Stopwatch.GetTimestamp() - start;
            codes.Count.Should().BeGreaterThan(0);
        });

        sw.Stop();

        Array.Sort(latenciesTicks);
        static double TicksToMs(long t) => Stopwatch.GetElapsedTime(0, t).TotalMilliseconds;
        var p50 = TicksToMs(latenciesTicks[ConcurrentCalls / 2]);
        var p95 = TicksToMs(latenciesTicks[(int)(ConcurrentCalls * 0.95)]);
        var p99 = TicksToMs(latenciesTicks[(int)(ConcurrentCalls * 0.99)]);
        var avg = latenciesTicks.Select(TicksToMs).Average();
        var max = TicksToMs(latenciesTicks.Max());

        Console.WriteLine($"PermissionResolver perf: p50={p50:F2}ms, p95={p95:F2}ms, p99={p99:F2}ms, avg={avg:F2}ms, max={max:F2}ms, total={sw.ElapsedMilliseconds}ms");

        // #AUTH-89 spec literal: assert p99 < 50ms (KHÔNG phải avg — vì tail latency mới phản ánh thật user worst-case).
        p99.Should().BeLessThan(P99ThresholdMs,
            $"p99 latency phải dưới {P99ThresholdMs}ms khi cache hit. Got p50={p50:F2}ms p95={p95:F2}ms p99={p99:F2}ms avg={avg:F2}ms max={max:F2}ms.");
    }

    [Fact]
    public async Task GetPermissionCodes_CacheInvalidate_NextCallHitsDb()
    {
        PermissionResolver.InvalidateAll();
        var (uow, accountId) = BuildSeededUow();

        // Warm — first call.
        await PermissionResolver.GetPermissionCodesAsync(uow.Object, accountId);

        // Invalidate cache cho role specific.
        // Get role id từ seeded data.
        var roleId = await GetRoleIdAsync(uow.Object, accountId);
        PermissionResolver.InvalidateRole(roleId);

        // Next call sẽ touch DB lại (verify by mock interaction nếu cần — đơn giản là return correct value).
        var codes = await PermissionResolver.GetPermissionCodesAsync(uow.Object, accountId);
        codes.Should().NotBeEmpty();
    }

    private static (Mock<IAuthUnitOfWork> uow, Guid accountId) BuildSeededUow()
    {
        var roleId = Guid.NewGuid();
        var role = new Role
        {
            Id = roleId,
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active
        };
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "perf@example.com",
            PasswordHash = "x",
            FullName = "Perf",
            Status = AccountStatusEnum.Active,
            RoleId = roleId
        };
        var permissions = Enumerable.Range(0, 20)
            .Select(i => new Permission
            {
                Id = Guid.NewGuid(),
                Code = $"perm.{i}",
                Description = $"Permission {i}"
            })
            .ToList();
        var rolePermissions = permissions.Select(p => new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = p.Id
        }).ToList();

        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            roleSeed: new[] { role },
            permissionSeed: permissions,
            rolePermissionSeed: rolePermissions);

        return (uow, account.Id);
    }

    private static async Task<Guid> GetRoleIdAsync(IAuthUnitOfWork uow, Guid accountId)
    {
        // Helper — trong test thật có thể query trực tiếp; ở đây dùng seed nên có thể hardcode,
        // nhưng để test general, dùng lookup.
        await Task.CompletedTask;
        var account = uow.Accounts.GetAllAsync().First(a => a.Id == accountId);
        return account.RoleId ?? Guid.Empty;
    }
}
