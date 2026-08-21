using FluentAssertions;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Phủ nốt các command của TicketService còn để trống ở tầng <c>ValidateAsync</c>.
///
/// <para>Nhóm Chat nhận dữ liệu tệp đính kèm và ghi âm trực tiếp từ client, nên luật validate ở đây
/// là hàng rào đầu tiên trước khi file đi vào kho lưu trữ và pipeline nhận dạng giọng nói.</para>
/// </summary>
public class ChatVoiceTranscribeCommandValidationTests
{
    private static ChatVoiceTranscribeCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        FileId = Guid.NewGuid(),
        FileName = "voice-note.m4a",
        ContentType = "audio/m4a",
        SizeBytes = 512_000,
        Url = "https://storage.example.com/voice-note.m4a"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyFileId_Fails()
    {
        var c = Valid();
        c.FileId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "FileId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingFileName_Fails(string fileName)
    {
        var c = Valid();
        c.FileName = fileName;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FileName");
    }

    /// <summary>Chỉ các định dạng âm thanh trong danh sách cho phép mới đi tiếp.</summary>
    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/wav")]
    [InlineData("audio/webm")]
    [InlineData("audio/flac")]
    [InlineData("AUDIO/MP3")]   // so sánh không phân biệt hoa thường
    public async Task AllowedAudioType_Passes(string contentType)
    {
        var c = Valid();
        c.ContentType = contentType;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "ContentType");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public async Task DisallowedAudioType_Fails(string contentType)
    {
        var c = Valid();
        c.ContentType = contentType;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ContentType");
    }

    /// <summary>Kích thước phải nằm trong khoảng 1 byte đến 20 MB.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ChatVoiceTranscribeCommand.MaxAudioFileSizeDefault + 1)]
    public async Task SizeOutOfRange_Fails(long size)
    {
        var c = Valid();
        c.SizeBytes = size;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "SizeBytes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(ChatVoiceTranscribeCommand.MaxAudioFileSizeDefault)]
    public async Task SizeAtBoundary_Passes(long size)
    {
        var c = Valid();
        c.SizeBytes = size;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "SizeBytes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingUrl_Fails(string url)
    {
        var c = Valid();
        c.Url = url;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Url");
    }
}

public class ChatAttachmentBatchAddCommandValidationTests
{
    private static AttachmentItem Item() => new()
    {
        FileId = Guid.NewGuid(),
        FileName = "photo.jpg",
        ContentType = "image/jpeg",
        SizeBytes = 2048,
        Url = "https://storage.example.com/photo.jpg"
    };

    private static ChatAttachmentBatchAddCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        ChatId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Files = [Item()]
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    /// <summary>Danh sách rỗng không có gì để đính kèm nên bị chặn ngay.</summary>
    [Fact]
    public async Task EmptyFileList_Fails()
    {
        var c = Valid();
        c.Files = [];

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Files");
    }

    [Fact]
    public async Task ItemWithEmptyFileId_Fails()
    {
        var c = Valid();
        var bad = Item(); bad.FileId = Guid.Empty;
        c.Files = [bad];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field.Contains("FileId"));
    }

    [Fact]
    public async Task ItemWithMissingFileName_Fails()
    {
        var c = Valid();
        var bad = Item(); bad.FileName = "  ";
        c.Files = [bad];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field.Contains("FileName"));
    }

    [Fact]
    public async Task ItemWithMissingContentType_Fails()
    {
        var c = Valid();
        var bad = Item(); bad.ContentType = "";
        c.Files = [bad];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field.Contains("ContentType"));
    }

    /// <summary>SizeBytes phải lớn hơn 0 — khác với attachment của ticket (nơi 0 hợp lệ).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task ItemWithNonPositiveSize_Fails(long size)
    {
        var c = Valid();
        var bad = Item(); bad.SizeBytes = size;
        c.Files = [bad];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field.Contains("SizeBytes"));
    }

    /// <summary>Lỗi được gắn theo chỉ số của từng phần tử để client biết dòng nào hỏng.</summary>
    [Fact]
    public async Task MultipleInvalidItems_ReportErrorPerIndex()
    {
        var c = Valid();
        var bad0 = Item(); bad0.FileId = Guid.Empty;
        var bad1 = Item(); bad1.SizeBytes = 0;
        c.Files = [bad0, bad1];

        var r = await c.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field.Contains("Files[0]"));
        r.ListErrors.Should().Contain(e => e.Field.Contains("Files[1]"));
    }
}

public class RemainingChatCommandValidationTests
{
    [Fact]
    public async Task ChatVoiceTranscriptionRetry_Valid_Passes()
    {
        var r = await new ChatVoiceTranscriptionRetryCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChatVoiceTranscriptionRetry_MissingIds_Fails()
    {
        var r = await new ChatVoiceTranscriptionRetryCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "chatId");
    }

    [Fact]
    public async Task ChatConvertToKbDraft_Valid_Passes()
    {
        var r = await new ChatConvertToKbDraftCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            Title = "How to reset the inverter"
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChatConvertToKbDraft_TitleTooLong_Fails()
    {
        var r = await new ChatConvertToKbDraftCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            Title = new string('t', 201)
        }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "Title");
    }

    /// <summary>Title là tuỳ chọn — bỏ trống thì không kiểm độ dài.</summary>
    [Fact]
    public async Task ChatConvertToKbDraft_NullTitle_Passes()
    {
        var r = await new ChatConvertToKbDraftCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            Title = null
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChatAttachKbReference_Valid_Passes()
    {
        var r = await new ChatAttachKbReferenceCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            KbArticleId = Guid.NewGuid(),
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChatAttachKbReference_EmptyKbArticleId_Fails()
    {
        var r = await new ChatAttachKbReferenceCommand
        {
            KbArticleId = Guid.Empty,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve
        }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "KbArticleId");
    }

    /// <summary>Giá trị enum ngoài dải định nghĩa bị từ chối.</summary>
    [Fact]
    public async Task ChatAttachKbReference_UndefinedReferenceType_Fails()
    {
        var r = await new ChatAttachKbReferenceCommand
        {
            KbArticleId = Guid.NewGuid(),
            ReferenceType = (KbReferenceTypeEnum)999
        }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "ReferenceType");
    }
}

public class RemainingTicketCommandValidationTests
{
    [Fact]
    public async Task TicketSchedule_Valid_Passes()
    {
        var r = await new TicketScheduleCommand
        {
            TicketId = Guid.NewGuid(),
            ScheduledStartAt = DateTimeOffset.UtcNow.AddDays(1),
            ManagerId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TicketSchedule_MissingFields_Fail()
    {
        var r = await new TicketScheduleCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "ScheduledStartAt");
    }

    [Fact]
    public async Task TicketReVerify_Valid_Passes()
    {
        var r = await new TicketReVerifyCommand
        {
            TicketId = Guid.NewGuid(),
            ManagerId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TicketReVerify_EmptyTicketId_Fails()
    {
        var r = await new TicketReVerifyCommand().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
    }
}
