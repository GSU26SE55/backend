using FluentAssertions;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketCreateCommandValidationTests
{
    [Fact]
    public async Task ValidateAsync_TwoBatteryIds_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.BatteryAssetIds = [Guid.NewGuid(), Guid.NewGuid()];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "BatteryAssetIds"
            && error.Detail.Contains("Exactly one battery"));
    }

    [Fact]
    public async Task ValidateAsync_OneBatteryId_IsValid()
    {
        var result = await ValidCommand().ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    /// <summary>
    /// Đúng một battery nhưng id rỗng — nhánh else-if sau kiểm tra Count.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SingleEmptyBatteryId_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.BatteryAssetIds = [Guid.Empty];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(error => error.Field == "BatteryAssetIds"
            && error.Detail.Contains("must not contain empty IDs"));
    }

    /// <summary>
    /// Danh sách battery rỗng cũng vi phạm luật "đúng một battery".
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NoBatteryId_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.BatteryAssetIds = [];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "BatteryAssetIds"
            && error.Detail.Contains("Exactly one battery"));
    }

    // ---------- Attachments ----------

    [Fact]
    public async Task ValidateAsync_AttachmentEmptyFileId_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { FileId = Guid.Empty }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.FileId"
            && error.Detail.Contains("Invalid FileId"));
    }

    /// <summary>FileName rỗng, whitespace, hoặc vượt 256 ký tự đều bị chặn.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ValidateAsync_AttachmentMissingFileName_ReturnsValidationError(string? fileName)
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { FileName = fileName! }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.FileName");
    }

    [Fact]
    public async Task ValidateAsync_AttachmentFileNameTooLong_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { FileName = new string('a', 257) }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.FileName"
            && error.Detail.Contains("at most 256 characters"));
    }

    /// <summary>Biên trên hợp lệ của FileName là đúng 256 ký tự.</summary>
    [Fact]
    public async Task ValidateAsync_AttachmentFileNameExactly256_IsValid()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { FileName = new string('a', 256) }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_AttachmentContentTypeTooLong_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { ContentType = new string('c', 101) }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.ContentType"
            && error.Detail.Contains("at most 100 characters"));
    }

    /// <summary>SizeBytes âm bị chặn; 0 là hợp lệ (file rỗng vẫn upload được).</summary>
    [Fact]
    public async Task ValidateAsync_AttachmentNegativeSize_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { SizeBytes = -1 }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.SizeBytes"
            && error.Detail.Contains("must not be negative"));
    }

    [Fact]
    public async Task ValidateAsync_AttachmentZeroSize_IsValid()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { SizeBytes = 0 }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_AttachmentUrlTooLong_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment() with { Url = "https://x/" + new string('u', 2000) }];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "Attachments.Url"
            && error.Detail.Contains("at most 2000 characters"));
    }

    /// <summary>
    /// Luật attachment chạy cho TỪNG phần tử: hai attachment hỏng sinh hai lỗi riêng,
    /// không dừng ở phần tử đầu.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_MultipleInvalidAttachments_CollectsErrorPerItem()
    {
        var command = ValidCommand();
        command.Attachments =
        [
            ValidAttachment() with { FileId = Guid.Empty },
            ValidAttachment() with { SizeBytes = -5 }
        ];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Attachments.FileId");
        result.ListErrors.Should().Contain(e => e.Field == "Attachments.SizeBytes");
    }

    [Fact]
    public async Task ValidateAsync_ValidAttachment_IsValid()
    {
        var command = ValidCommand();
        command.Attachments = [ValidAttachment()];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    private static TicketAttachmentInput ValidAttachment() => new(
        FileId: Guid.NewGuid(),
        FileName: "photo.jpg",
        ContentType: "image/jpeg",
        SizeBytes: 1024,
        Url: "https://storage.example.com/photo.jpg");

    private static TicketCreateCommand ValidCommand() => new()
    {
        Title = "Battery maintenance",
        Description = "Battery requires maintenance.",
        Category = TicketCategoryEnum.Repair,
        CustomerId = Guid.NewGuid(),
        BatteryAssetIds = [Guid.NewGuid()],
        IncidentDetectedAt = DateTime.UtcNow.AddMinutes(-1)
    };
}
