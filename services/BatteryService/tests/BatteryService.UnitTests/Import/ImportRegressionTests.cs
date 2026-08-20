using System.Reflection;
using BatteryService.Application.Import;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.UnitTests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Import;

/// <summary>
/// Hồi quy cho hai lỗi chỉ lộ ra khi chạy thật, không lỗi nào bị unit test cũ bắt được.
/// </summary>
public class ImportRegressionTests
{
    // ---------- Lỗi 1: một lô đang chờ chặn đứng mọi lô nạp sau ----------

    /// <summary>
    /// Bản đầu của tiến trình nền chỉ lấy <b>lô cũ nhất</b> mỗi nhịp. Hệ quả: một lô đang chờ tài
    /// khoản đồng bộ giữ chỗ vô thời hạn, và mọi lô nạp sau nó đứng im cho tới khi nó hết hạn chờ.
    /// Người dùng thứ hai chỉ thấy lô của mình không nhúc nhích, không có lý do nào hiện ra.
    /// </summary>
    [Fact]
    public async Task Processor_AdvancesEveryPendingBatch_NotJustTheOldestOne()
    {
        var stuck = new ImportBatch
        {
            Id = Guid.NewGuid(),
            Status = ImportBatchStatusEnum.AwaitingAccountSync,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        var newer = new ImportBatch
        {
            Id = Guid.NewGuid(),
            Status = ImportBatchStatusEnum.Committing,
            CreatedAt = DateTime.UtcNow
        };

        var advanced = new List<Guid>();
        var commitService = new Mock<IImportCommitService>();
        commitService
            .Setup(service => service.AdvanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => advanced.Add(id))
            // Lô cũ vẫn đang chờ nên trả về "chưa xong" — đúng tình huống đã gây ra lỗi.
            .ReturnsAsync((Guid id, CancellationToken _) => id != stuck.Id);

        var processor = BuildProcessor(commitService.Object, stuck, newer);

        await processor.RunOnceAsync(CancellationToken.None);

        advanced.Should().Contain(stuck.Id);
        advanced.Should().Contain(newer.Id,
            "a batch waiting for account sync must not block batches queued after it");
    }

    [Fact]
    public async Task Processor_OneFailingBatch_DoesNotStopTheRest()
    {
        var failing = new ImportBatch
        {
            Id = Guid.NewGuid(),
            Status = ImportBatchStatusEnum.Committing,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var healthy = new ImportBatch
        {
            Id = Guid.NewGuid(),
            Status = ImportBatchStatusEnum.Committing,
            CreatedAt = DateTime.UtcNow
        };

        var advanced = new List<Guid>();
        var commitService = new Mock<IImportCommitService>();
        commitService
            .Setup(service => service.AdvanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => advanced.Add(id))
            .Returns<Guid, CancellationToken>((id, _) => id == failing.Id
                ? Task.FromException<bool>(new InvalidOperationException("boom"))
                : Task.FromResult(true));

        var processor = BuildProcessor(commitService.Object, failing, healthy);

        // Lỗi của một lô không được thoát ra ngoài và cũng không được bỏ rơi lô còn lại.
        var act = async () => await processor.RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        advanced.Should().Contain(healthy.Id);
    }

    [Fact]
    public async Task Processor_NoPendingBatch_DoesNothing()
    {
        var commitService = new Mock<IImportCommitService>();
        var processor = BuildProcessor(commitService.Object);

        await processor.RunOnceAsync(CancellationToken.None);

        commitService.Verify(
            service => service.AdvanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Lỗi 2: [FromForm] trên IFormFile làm vỡ toàn bộ tài liệu Swagger ----------

    /// <summary>
    /// Gắn <c>[FromForm]</c> trực tiếp lên tham số <c>IFormFile</c> khiến bộ sinh tài liệu ném lỗi.
    /// Nó không chỉ hỏng một endpoint: <c>/swagger/v1/swagger.json</c> trả 500 cho <b>cả service</b>,
    /// nên mọi endpoint khác cũng biến mất khỏi tài liệu. Endpoint vẫn gọi được, nên lỗi này không
    /// làm test nào đỏ và chỉ lộ ra khi có người mở trang tài liệu.
    /// </summary>
    [Fact]
    public void NoControllerAction_PutsFromFormAttributeDirectlyOnAnIFormFileParameter()
    {
        var offenders = ApiActions()
            .SelectMany(action => action.GetParameters()
                .Where(parameter => IsFormFile(parameter.ParameterType)
                                    && parameter.GetCustomAttribute<FromFormAttribute>() is not null)
                .Select(parameter => $"{action.DeclaringType!.Name}.{action.Name}({parameter.Name})"))
            .ToList();

        offenders.Should().BeEmpty(
            "[FromForm] on an IFormFile parameter breaks Swagger generation for the whole service; "
            + "ASP.NET already binds IFormFile from the form without it");
    }

    [Fact]
    public void EveryFileUploadAction_DeclaresTheMultipartContentType()
    {
        var missing = ApiActions()
            .Where(action => action.GetParameters().Any(parameter => IsFormFile(parameter.ParameterType)))
            .Where(action => action.GetCustomAttribute<ConsumesAttribute>() is not { } consumes
                             || !consumes.ContentTypes.Contains("multipart/form-data"))
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToList();

        missing.Should().BeEmpty(
            "an action taking IFormFile must declare [Consumes(\"multipart/form-data\")] so the "
            + "generated documentation shows a file picker instead of a JSON body");
    }

    private static bool IsFormFile(Type type) =>
        type == typeof(IFormFile) || type == typeof(IFormFileCollection)
        || (type.IsArray && type.GetElementType() == typeof(IFormFile));

    private static IEnumerable<MethodInfo> ApiActions() =>
        typeof(Api.Controllers.ImportsController).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static TestableProcessor BuildProcessor(IImportCommitService commitService, params ImportBatch[] batches)
    {
        var unitOfWork = new MockUnitOfWorkBuilder().WithImportBatches(batches).Build();

        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddSingleton(commitService);

        return new TestableProcessor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportOptions()),
            NullLogger<ImportBatchProcessorBackgroundService>.Instance);
    }

    /// <summary>Mở lối gọi thẳng một nhịp xử lý, không phải dựng cả vòng lặp nền.</summary>
    private sealed class TestableProcessor : ImportBatchProcessorBackgroundService
    {
        public TestableProcessor(
            IServiceScopeFactory scopeFactory,
            IOptions<ImportOptions> options,
            NullLogger<ImportBatchProcessorBackgroundService> logger)
            : base(scopeFactory, options, logger)
        {
        }

        public Task RunOnceAsync(CancellationToken cancellationToken) => ProcessOneBatchAsync(cancellationToken);
    }
}
