using System.Reflection;
using System.Text.Json;
using BatteryService.Api.Controllers;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Handler.Import;
using BatteryService.Application.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Import;

/// <summary>I7 — hoàn tác lô. I9 — hồi quy ghi outbox. I12 — hợp đồng phân quyền.</summary>
public class ImportRevertAndContractTests
{
    // ---------- I7: hoàn tác ----------

    [Fact]
    public async Task Revert_RemovesOnlyWhatTheBatchCreated_InReverseOrder()
    {
        var batchId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var untouchedSiteId = Guid.NewGuid();

        var batch = new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.Completed };
        var rows = new[]
        {
            NewRow(batchId, ImportEntityTypeEnum.Site, siteId, ImportRowStatusEnum.Created),
            NewRow(batchId, ImportEntityTypeEnum.BatteryAsset, assetId, ImportRowStatusEnum.Created)
        };

        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(batch)
            .WithImportRows(rows)
            .WithSites(
                new Site { Id = siteId, Name = "Imported", CustomerId = Guid.NewGuid() },
                new Site { Id = untouchedSiteId, Name = "Pre-existing", CustomerId = Guid.NewGuid() })
            .WithBatteryAssets(new BatteryAsset { Id = assetId, SerialNumber = "SER-1" })
            .WithImportEntityLinks(new ImportEntityLink
            {
                Id = Guid.NewGuid(),
                EntityType = ImportEntityTypeEnum.Site,
                ExternalRef = "ST-001",
                ExternalRefRaw = "ST-001",
                InternalId = siteId,
                CreatedByBatchId = batchId
            });

        var order = new List<string>();
        builder.BatteryAssets.Setup(r => r.DeleteAsync(It.IsAny<BatteryAsset>())).Callback(() => order.Add("asset"));
        builder.Sites.Setup(r => r.DeleteAsync(It.IsAny<Site>())).Callback<Site>(site =>
        {
            order.Add("site");
            // Bản ghi không thuộc lô phải nguyên vẹn.
            site.Id.Should().NotBe(untouchedSiteId);
        });

        var handler = new RevertImportBatchCommandHandler(builder.Build(), Mock.Of<IPublisher>());

        var result = await handler.Handle(new RevertImportBatchCommand { Id = batchId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Thứ tự ngược bắt buộc: đi xuôi sẽ vấp khoá ngoại vì site vẫn còn pin trỏ tới.
        order.Should().Equal("asset", "site");
        // Luồng nhập không tạo thiết bị IoT, nên hoàn tác cũng không được đụng vào chúng.
        builder.IotDevices.Verify(repository => repository.DeleteAsync(It.IsAny<IotDevice>()), Times.Never);
        batch.Status.Should().Be(ImportBatchStatusEnum.Reverted);
        result.Message.Should().Contain("Customer accounts were intentionally kept");
    }

    [Fact]
    public async Task Revert_DoesNotRemoveRowsThatOnlyUpdatedExistingRecords()
    {
        var batchId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.Completed })
            .WithImportRows(NewRow(batchId, ImportEntityTypeEnum.Site, siteId, ImportRowStatusEnum.Updated))
            .WithSites(new Site { Id = siteId, Name = "Existing", CustomerId = Guid.NewGuid() });

        var handler = new RevertImportBatchCommandHandler(builder.Build(), Mock.Of<IPublisher>());

        await handler.Handle(new RevertImportBatchCommand { Id = batchId }, CancellationToken.None);

        // Dòng cập nhật ghi đè lên bản ghi có sẵn từ trước; xoá nó đi là xoá dữ liệu không thuộc lô.
        builder.Sites.Verify(repository => repository.DeleteAsync(It.IsAny<Site>()), Times.Never);
    }

    [Fact]
    public async Task Revert_OnABatchStillRunning_IsRejected()
    {
        var batchId = Guid.NewGuid();
        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.Committing });

        var handler = new RevertImportBatchCommandHandler(builder.Build(), Mock.Of<IPublisher>());

        var result = await handler.Handle(new RevertImportBatchCommand { Id = batchId }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    // ---------- I9: hồi quy ghi outbox ----------

    [Fact]
    public async Task Outbox_ReceivesTheConcreteEventType_NotTheBaseType()
    {
        // Bộ ghi outbox lấy tên loại từ tham số generic. Truyền một biến khai kiểu cha thì cột
        // `type` lưu tên kiểu cha, và bộ chuyển tiếp sẽ báo "unknown event type" rồi bỏ qua —
        // sự kiện chết im lặng, không ngoại lệ nào nổi lên.
        var captured = new List<Type>();
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox
            .Setup(writer => writer.WriteAsync(It.IsAny<PartnerCustomerProvisionRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerCustomerProvisionRequestedEvent, CancellationToken>((evt, _) => captured.Add(evt.GetType()))
            .Returns(Task.CompletedTask);

        var batchId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(new ImportBatch
            {
                Id = batchId,
                Status = ImportBatchStatusEnum.Committing,
                StartedAt = DateTime.UtcNow
            })
            .WithImportRows(new ImportRow
            {
                Id = rowId,
                ImportBatchId = batchId,
                EntityType = ImportEntityTypeEnum.Customer,
                Status = ImportRowStatusEnum.Valid,
                RowNumber = 2,
                ExternalRef = "KH-001",
                RawJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["external_customer_code"] = "KH-001",
                    ["full_name"] = "Cong ty A",
                    ["email"] = "a@example.com"
                })
            });

        var service = new ImportCommitService(
            builder.Build(),
            new ImportRowValidator(Microsoft.Extensions.Options.Options.Create(new ImportOptions())),
            new BatteryTypeResolver(builder.Build()),
            outbox.Object,
            Microsoft.Extensions.Options.Options.Create(new ImportOptions()),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ImportCommitService>>());

        await service.AdvanceAsync(batchId, CancellationToken.None);

        captured.Should().ContainSingle();
        captured[0].Should().Be<PartnerCustomerProvisionRequestedEvent>();
        captured[0].Should().NotBe(typeof(IntegrationEvent));
    }

    // ---------- I12: hợp đồng phân quyền ----------

    /// <summary>
    /// Toàn bộ endpoint nhập dữ liệu chỉ dành cho Admin. Đây là ràng buộc nghiệp vụ, không phải
    /// mặc định kỹ thuật: luồng nhập tạo ra tài khoản khách hàng ở AuthService và có thể gỡ hàng
    /// loạt bản ghi khi hoàn tác, nên nó thuộc quyền quản trị hệ thống chứ không phải điều hành.
    /// </summary>
    [Theory]
    [InlineData(nameof(ImportsController.CreateBatch), "Admin")]
    [InlineData(nameof(ImportsController.GetBatches), "Admin")]
    [InlineData(nameof(ImportsController.GetBatch), "Admin")]
    [InlineData(nameof(ImportsController.GetRows), "Admin")]
    [InlineData(nameof(ImportsController.UpdateRows), "Admin")]
    [InlineData(nameof(ImportsController.GetErrorsCsv), "Admin")]
    [InlineData(nameof(ImportsController.GetTemplate), "Admin")]
    [InlineData(nameof(ImportsController.Commit), "Admin")]
    [InlineData(nameof(ImportsController.Revert), "Admin")]
    public void ImportsController_EveryActionDeclaresTheExpectedRoles(string actionName, string expectedRoles)
    {
        var method = typeof(ImportsController).GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<AuthorizeAttribute>();
        attribute.Should().NotBeNull($"{actionName} must be role-gated");
        attribute!.Roles.Should().Be(expectedRoles);
    }

    [Fact]
    public void ImportsController_DoesNotUseTheDeadAdminOnlyPolicy()
    {
        // Chính sách "AdminOnly" từng được nhắc trong tài liệu cũ nhưng chưa bao giờ được đăng ký ở
        // service này. Dùng nó thì mọi request đều bị chặn, và triệu chứng là 403 không lý do.
        var attributes = typeof(ImportsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>())
            .Concat(typeof(ImportsController).GetCustomAttributes<AuthorizeAttribute>());

        attributes.Should().NotContain(attribute => attribute.Policy == "AdminOnly");
    }

    private static ImportRow NewRow(Guid batchId, ImportEntityTypeEnum entityType, Guid entityId, ImportRowStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        ImportBatchId = batchId,
        EntityType = entityType,
        Status = status,
        CreatedEntityId = entityId,
        RowNumber = 2,
        ExternalRef = "REF-1",
        RawJson = "{}"
    };
}
