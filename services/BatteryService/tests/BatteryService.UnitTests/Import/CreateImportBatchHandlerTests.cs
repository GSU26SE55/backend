using System.Text;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Handler.Import;
using BatteryService.Application.Import;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Import;

/// <summary>I5, I6 — bước kiểm định không được ghi dữ liệu nghiệp vụ, và phải chặn file nạp lại.</summary>
public class CreateImportBatchHandlerTests
{
    private const string CustomersCsv =
        "external_customer_code,full_name,email,phone\n" +
        "KH-001,Cong ty A,a@example.com,0901234567\n" +
        "KH-002,Cong ty B,b@example.com,0901234568\n";

    private const string SitesCsv =
        "external_site_code,external_customer_code,site_name,address\n" +
        "ST-001,KH-001,Nha may Long An,KCN Long An\n";

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    private static (CreateImportBatchCommandHandler Handler, MockUnitOfWorkBuilder Builder) Build(
        params ImportBatch[] existingBatches)
    {
        var builder = new MockUnitOfWorkBuilder().WithImportBatches(existingBatches);
        var handler = new CreateImportBatchCommandHandler(
            builder.Build(),
            new CsvImportFileParser(),
            new ImportRowValidator(Options.Create(new ImportOptions())),
            Options.Create(new ImportOptions()));

        return (handler, builder);
    }

    [Fact]
    public async Task Handle_ValidFiles_CreatesBatchWithoutTouchingBusinessTables()
    {
        var (handler, builder) = Build();

        var result = await handler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(CustomersCsv),
            SitesCsv = Bytes(SitesCsv),
            FileName = "handover.csv"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.TotalRows.Should().Be(3);
        result.Data.ValidRows.Should().Be(3);
        result.Data.InvalidRows.Should().Be(0);
        result.Data.Status.Should().Be(ImportBatchStatusEnum.ReadyToCommit);

        // Điểm mấu chốt của bước chạy thử: không một bản ghi nghiệp vụ nào được tạo.
        builder.Sites.Verify(repository => repository.AddAsync(It.IsAny<Site>()), Times.Never);
        builder.BatteryAssets.Verify(repository => repository.AddAsync(It.IsAny<BatteryAsset>()), Times.Never);
        // Thiết bị IoT không nằm trong phạm vi nhập dữ liệu — chúng do hệ thống cấp phát cùng khoá
        // API và credential MQTT. Khẳng định này giữ lại như một chốt chặn cho mọi thay đổi sau.
        builder.IotDevices.Verify(repository => repository.AddAsync(It.IsAny<IotDevice>()), Times.Never);
        builder.CustomerAccounts.Verify(repository => repository.AddAsync(It.IsAny<CustomerAccount>()), Times.Never);
        builder.ImportEntityLinks.Verify(repository => repository.AddAsync(It.IsAny<ImportEntityLink>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoFiles_IsRejectedByValidation()
    {
        var command = new CreateImportBatchCommand();

        var validation = await command.ValidateAsync();

        validation.IsSuccess.Should().BeFalse();
        validation.StatusCode.Should().Be(400);
        validation.ListErrors.Should().Contain(error => error.Field == "files");
    }

    [Fact]
    public async Task Handle_SameContentUploadedTwice_IsRejectedForAdminUpload()
    {
        var (firstHandler, _) = Build();
        var first = await firstHandler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(CustomersCsv)
        }, CancellationToken.None);

        var stored = new ImportBatch
        {
            Id = Guid.Parse(first.Data!.Id),
            FileSha256 = ExtractHash(),
            Status = ImportBatchStatusEnum.ReadyToCommit,
            CreatedAt = DateTime.UtcNow
        };

        var (secondHandler, _) = Build(stored);
        var second = await secondHandler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(CustomersCsv)
        }, CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
    }


    [Fact]
    public async Task Handle_SiteReferencingUnknownCustomer_MarksOnlyThatRowInvalid()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(CustomersCsv),
            SitesCsv = Bytes(
                "external_site_code,external_customer_code,site_name\n" +
                "ST-001,KH-001,Nha may Long An\n" +
                "ST-002,KH-999,Nha may Ma\n")
        }, CancellationToken.None);

        result.Data!.TotalRows.Should().Be(4);
        result.Data.InvalidRows.Should().Be(1);
        result.Data.ValidRows.Should().Be(3);
    }

    [Fact]
    public async Task Handle_DuplicateReferenceInsideOneBatch_MarksBothRowsInvalid()
    {
        // Hai dòng cùng mã là lỗi dữ liệu nguồn. Im lặng chọn một dòng sẽ khiến đối tác không bao
        // giờ biết dữ liệu bên họ có vấn đề.
        var (handler, _) = Build();

        var result = await handler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(
                "external_customer_code,full_name,email\n" +
                "KH-001,Cong ty A,a@example.com\n" +
                "KH-001,Cong ty A lan hai,a2@example.com\n")
        }, CancellationToken.None);

        result.Data!.TotalRows.Should().Be(2);
        result.Data.InvalidRows.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ReferenceCodeOverMaxLength_StoresATruncatedRefInsteadOfCrashing()
    {
        // Bug thật gặp qua e2e (2026-09-01): dòng hỏng CHÍNH VÌ mã tham chiếu quá 128 ký tự vẫn bị
        // lưu nguyên văn (chưa cắt) vào cột ExternalRef varchar(128) — PostgresException 22001 đánh
        // sập TOÀN BỘ lô (500) thay vì chỉ đánh hỏng đúng 1 dòng.
        var (handler, builder) = Build();
        var overLongCode = new string('X', 130);

        var captured = new List<ImportRow>();
        builder.ImportRows
            .Setup(repository => repository.AddAsync(It.IsAny<ImportRow>()))
            .Callback<ImportRow>(row => captured.Add(row))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes(
                "external_customer_code,full_name,email\n" +
                $"{overLongCode},Cong ty A,a@example.com\n"),
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.InvalidRows.Should().Be(1);
        captured.Should().ContainSingle();
        captured[0].ExternalRef.Length.Should().BeLessOrEqualTo(128);
    }

    [Fact]
    public async Task Handle_FileMissingRequiredColumn_MarksTheWholeBatchAsValidationFailed()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new CreateImportBatchCommand
        {
            CustomersCsv = Bytes("external_customer_code,full_name\nKH-001,Cong ty A\n")
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        result.Data!.Status.Should().Be(ImportBatchStatusEnum.ValidationFailed);
        result.Message.Should().Contain("email");
    }

    /// <summary>
    /// Lấy lại mã băm mà bộ xử lý đã tính, bằng cách chạy đúng thuật toán đó trên cùng nội dung.
    /// </summary>
    private static string ExtractHash()
    {
        // Mã băm không nằm trong DTO (nó là chi tiết bên trong), nên dựng lại từ chính nội dung đã gửi.
        using var buffer = new MemoryStream();
        void Append(byte[]? content)
        {
            var length = BitConverter.GetBytes(content?.Length ?? 0);
            buffer.Write(length, 0, length.Length);
            if (content is { Length: > 0 })
                buffer.Write(content, 0, content.Length);
        }

        // Đúng ba phần, theo đúng thứ tự mà bộ xử lý ghép: khách hàng, site, pin. Lệch số phần là
        // ra mã băm khác, và phép chặn nạp trùng sẽ không nhận ra file đã nạp.
        Append(Bytes(CustomersCsv));
        Append(null);
        Append(null);

        buffer.Position = 0;
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(buffer)).ToLowerInvariant();
    }
}
