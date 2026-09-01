using System.Text.Json;
using BatteryService.Application.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Import;

/// <summary>Pha ghi thật — bậc Site.</summary>
public class ImportCommitServiceTests
{
    private static ImportRow NewSiteRow(Guid batchId, string externalSiteCode, string externalCustomerCode, string siteName)
    {
        var rawJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["external_site_code"] = externalSiteCode,
            ["external_customer_code"] = externalCustomerCode,
            ["site_name"] = siteName,
        });

        return new ImportRow
        {
            Id = Guid.NewGuid(),
            ImportBatchId = batchId,
            EntityType = ImportEntityTypeEnum.Site,
            Status = ImportRowStatusEnum.Valid,
            RowNumber = 2,
            ExternalRef = externalSiteCode,
            RawJson = rawJson,
        };
    }

    private static ImportCommitService BuildService(MockUnitOfWorkBuilder builder) => new(
        builder.Build(),
        new ImportRowValidator(Options.Create(new ImportOptions())),
        new BatteryTypeResolver(builder.Build()),
        Mock.Of<IIntegrationEventOutboxWriter>(),
        Options.Create(new ImportOptions()),
        NullLogger<ImportCommitService>.Instance);

    [Fact]
    public async Task AdvanceAsync_TwoSiteRowsSameNameSameCustomerInOneBatch_OnlyTheSecondFails()
    {
        // Bug thật gặp qua e2e (2026-09-01): dòng site thứ hai trùng tên với dòng thứ nhất TRONG
        // CÙNG lô không bị truy vấn DB phát hiện (dòng đầu chưa lưu xuống) — cả hai cùng AddAsync
        // rồi vỡ ràng buộc duy nhất IX_sites_customer_id_name lúc SaveChanges, đánh sập TOÀN BỘ
        // lượt tiến độ (kể cả các dòng khác trong cùng lô) thay vì chỉ đánh hỏng đúng dòng thứ hai.
        //
        // Repo (Moq) không mô phỏng ràng buộc DB thật nên không tự vỡ như Postgres — bài kiểm này
        // xác nhận HÀNH VI đúng (dòng 2 bị đánh hỏng rõ ràng bởi bộ nhớ tạm, không lọt qua) thay vì
        // dựa vào việc mock có ném ngoại lệ hay không.
        var batchId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var row1 = NewSiteRow(batchId, "ST-001", "CUST-001", "Nha May Trung Ten");
        var row2 = NewSiteRow(batchId, "ST-002", "CUST-001", "Nha May Trung Ten");

        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(new ImportBatch
            {
                Id = batchId, Status = ImportBatchStatusEnum.Committing, StartedAt = DateTime.UtcNow,
            })
            .WithImportRows(row1, row2)
            .WithCustomerAccounts(new CustomerAccount { Id = customerId, IsActive = true })
            .WithImportEntityLinks(new ImportEntityLink
            {
                Id = Guid.NewGuid(),
                EntityType = ImportEntityTypeEnum.Customer,
                ExternalRef = "CUST-001",
                ExternalRefRaw = "CUST-001",
                InternalId = customerId,
            });

        var service = BuildService(builder);

        await service.AdvanceAsync(batchId, CancellationToken.None);

        row1.Status.Should().Be(ImportRowStatusEnum.Created);
        row2.Status.Should().Be(ImportRowStatusEnum.Failed);
        row2.ErrorsJson.Should().Contain("SiteName");
        builder.Sites.Verify(repository => repository.AddAsync(It.IsAny<Site>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceAsync_TwoSiteRowsDifferentNameSameCustomer_BothCreated()
    {
        // Đối chứng: hai site KHÁC tên dưới cùng một khách trong cùng lô vẫn phải tạo được cả hai —
        // bộ nhớ tạm chống trùng không được chặn nhầm những dòng hợp lệ.
        var batchId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var builder = new MockUnitOfWorkBuilder()
            .WithImportBatches(new ImportBatch
            {
                Id = batchId, Status = ImportBatchStatusEnum.Committing, StartedAt = DateTime.UtcNow,
            })
            .WithImportRows(
                NewSiteRow(batchId, "ST-001", "CUST-001", "Nha May A"),
                NewSiteRow(batchId, "ST-002", "CUST-001", "Nha May B"))
            .WithCustomerAccounts(new CustomerAccount { Id = customerId, IsActive = true })
            .WithImportEntityLinks(new ImportEntityLink
            {
                Id = Guid.NewGuid(),
                EntityType = ImportEntityTypeEnum.Customer,
                ExternalRef = "CUST-001",
                ExternalRefRaw = "CUST-001",
                InternalId = customerId,
            });

        var service = BuildService(builder);

        await service.AdvanceAsync(batchId, CancellationToken.None);

        builder.Sites.Verify(repository => repository.AddAsync(It.IsAny<Site>()), Times.Exactly(2));
    }
}
