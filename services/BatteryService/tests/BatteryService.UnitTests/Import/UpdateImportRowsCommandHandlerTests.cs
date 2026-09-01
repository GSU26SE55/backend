using System.Text.Json;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Handler.Import;
using BatteryService.Application.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Import;

/// <summary>Sửa dòng hỏng ngay trên giao diện rồi kiểm định lại — không cần tải lại cả file.</summary>
public class UpdateImportRowsCommandHandlerTests
{
    private static ImportRow NewRow(
        Guid batchId, ImportEntityTypeEnum entityType, string externalRef,
        Dictionary<string, string> fields, ImportRowStatusEnum status = ImportRowStatusEnum.Invalid)
    {
        return new ImportRow
        {
            Id = Guid.NewGuid(),
            ImportBatchId = batchId,
            EntityType = entityType,
            Status = status,
            RowNumber = 2,
            ExternalRef = externalRef,
            RawJson = JsonSerializer.Serialize(fields),
        };
    }

    private static (UpdateImportRowsCommandHandler Handler, MockUnitOfWorkBuilder Builder) Build(
        params ImportBatch[] batches)
    {
        var builder = new MockUnitOfWorkBuilder().WithImportBatches(batches);
        var handler = new UpdateImportRowsCommandHandler(
            builder.Build(),
            new ImportRowValidator(Options.Create(new ImportOptions())));

        return (handler, builder);
    }

    [Fact]
    public async Task Handle_BatchNotFound_Returns404()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = Guid.NewGuid(),
            Rows = [new ImportRowEditItem { RowId = Guid.NewGuid(), Fields = new() { ["x"] = "y" } }],
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_BatchNotReadyToCommit_Returns409()
    {
        var batchId = Guid.NewGuid();
        var (handler, _) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.Committing });

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows = [new ImportRowEditItem { RowId = Guid.NewGuid(), Fields = new() { ["x"] = "y" } }],
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("ReadyToCommit");
    }

    [Fact]
    public async Task Handle_UnknownRowId_Returns400()
    {
        var batchId = Guid.NewGuid();
        var (handler, builder) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.ReadyToCommit });
        builder.WithImportRows(NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-001",
            new() { ["external_customer_code"] = "KH-001", ["full_name"] = "", ["email"] = "a@example.com" }));

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows = [new ImportRowEditItem { RowId = Guid.NewGuid(), Fields = new() { ["x"] = "y" } }],
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_FixingTheMissingField_TurnsTheRowValid()
    {
        var batchId = Guid.NewGuid();
        var row = NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-001",
            new() { ["external_customer_code"] = "KH-001", ["full_name"] = "", ["email"] = "a@example.com" });

        var (handler, builder) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.ReadyToCommit });
        builder.WithImportRows(row);

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows =
            [
                new ImportRowEditItem
                {
                    RowId = row.Id,
                    Fields = new()
                    {
                        ["external_customer_code"] = "KH-001",
                        ["full_name"] = "Cong ty A",
                        ["email"] = "a@example.com",
                    },
                },
            ],
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.ValidRows.Should().Be(1);
        result.Data.InvalidRows.Should().Be(0);
        row.Status.Should().Be(ImportRowStatusEnum.Valid);
        row.ErrorsJson.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EditingIntroducesANewError_RowStaysInvalidWithTheNewReason()
    {
        var batchId = Guid.NewGuid();
        var row = NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-001",
            new() { ["external_customer_code"] = "KH-001", ["full_name"] = "", ["email"] = "a@example.com" });

        var (handler, builder) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.ReadyToCommit });
        builder.WithImportRows(row);

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows =
            [
                new ImportRowEditItem
                {
                    RowId = row.Id,
                    Fields = new()
                    {
                        ["external_customer_code"] = "KH-001",
                        ["full_name"] = "Cong ty A",
                        ["email"] = "khong-phai-email", // sửa xong vẫn sai, nhưng sai KIỂU KHÁC
                    },
                },
            ],
        }, CancellationToken.None);

        row.Status.Should().Be(ImportRowStatusEnum.Invalid);
        row.ErrorsJson.Should().Contain("Email").And.NotContain("FullName");
        result.Data!.InvalidRows.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FixingCustomerCode_AlsoResolvesADependentSiteRow_NotJustTheEditedOne()
    {
        // Đúng yêu cầu: sửa 1 dòng phải kiểm định lại CẢ LÔ, vì dòng site khác (không được sửa) có
        // thể chỉ hỏng vì dòng khách hàng nó tham chiếu tới đang sai.
        var batchId = Guid.NewGuid();
        var customerRow = NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-001",
            new() { ["external_customer_code"] = "KH-001", ["full_name"] = "", ["email"] = "a@example.com" });
        var siteRow = NewRow(batchId, ImportEntityTypeEnum.Site, "ST-001",
            new()
            {
                ["external_site_code"] = "ST-001",
                ["external_customer_code"] = "KH-001",
                ["site_name"] = "Nha may A",
            });

        var (handler, builder) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.ReadyToCommit });
        builder.WithImportRows(customerRow, siteRow);

        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows =
            [
                new ImportRowEditItem
                {
                    RowId = customerRow.Id, // chỉ sửa dòng khách hàng
                    Fields = new()
                    {
                        ["external_customer_code"] = "KH-001",
                        ["full_name"] = "Cong ty A",
                        ["email"] = "a@example.com",
                    },
                },
            ],
        }, CancellationToken.None);

        customerRow.Status.Should().Be(ImportRowStatusEnum.Valid);
        // Dòng site KHÔNG nằm trong yêu cầu sửa, nhưng phải tự hết lỗi "not found" vì khách hàng nó
        // trỏ tới giờ đã hợp lệ.
        siteRow.Status.Should().Be(ImportRowStatusEnum.Valid);
        result.Data!.ValidRows.Should().Be(2);
        result.Data.InvalidRows.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EditingCreatesADuplicateWithAnotherRow_BothMarkedInvalid()
    {
        var batchId = Guid.NewGuid();
        var row1 = NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-002", new()
        {
            ["external_customer_code"] = "KH-002",
            ["full_name"] = "Cong ty B",
            ["email"] = "b@example.com",
        }, ImportRowStatusEnum.Valid);
        var row2 = NewRow(batchId, ImportEntityTypeEnum.Customer, "KH-001",
            new() { ["external_customer_code"] = "KH-001", ["full_name"] = "", ["email"] = "a@example.com" });

        var (handler, builder) = Build(new ImportBatch { Id = batchId, Status = ImportBatchStatusEnum.ReadyToCommit });
        builder.WithImportRows(row1, row2);

        // Sửa dòng 2 nhưng lỡ gõ trùng mã với dòng 1.
        var result = await handler.Handle(new UpdateImportRowsCommand
        {
            BatchId = batchId,
            Rows =
            [
                new ImportRowEditItem
                {
                    RowId = row2.Id,
                    Fields = new()
                    {
                        ["external_customer_code"] = "KH-002",
                        ["full_name"] = "Cong ty A",
                        ["email"] = "a@example.com",
                    },
                },
            ],
        }, CancellationToken.None);

        row1.Status.Should().Be(ImportRowStatusEnum.Invalid, "dòng vốn valid giờ bị kéo theo vì trùng mã với dòng vừa sửa");
        row2.Status.Should().Be(ImportRowStatusEnum.Invalid);
        result.Data!.InvalidRows.Should().Be(2);
    }

    [Fact]
    public async Task ValidateAsync_NoRows_Fails()
    {
        var command = new UpdateImportRowsCommand { BatchId = Guid.NewGuid() };

        var validation = await command.ValidateAsync();

        validation.IsSuccess.Should().BeFalse();
        validation.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRowIdInSameRequest_Fails()
    {
        var rowId = Guid.NewGuid();
        var command = new UpdateImportRowsCommand
        {
            BatchId = Guid.NewGuid(),
            Rows =
            [
                new ImportRowEditItem { RowId = rowId, Fields = new() { ["a"] = "1" } },
                new ImportRowEditItem { RowId = rowId, Fields = new() { ["a"] = "2" } },
            ],
        };

        var validation = await command.ValidateAsync();

        validation.IsSuccess.Should().BeFalse();
        validation.ListErrors.Should().Contain(e => e.Field == "rows");
    }
}
