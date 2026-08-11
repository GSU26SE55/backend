using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Persistence.Seeders;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// Sprint 6.3 NOTI3-12 (#712) — danh mục template phủ đủ 32 type × kênh.
///
/// Trước sprint này chỉ 5/32 type có template trong DB; các type còn lại dùng chuỗi ghi cứng trong
/// consumer, nghĩa là sửa một câu chữ phải build lại và deploy lại.
/// </summary>
public class NotificationTemplateCatalogTests
{
    private static readonly IReadOnlyList<NotificationTemplateCatalog.Entry> Catalog =
        NotificationTemplateCatalog.Build(NotificationDispatchOptions.DefaultTypeChannelMatrix);

    /// <summary>Test bao: mỗi ô của ma trận type × kênh phải có template tiếng Việt.</summary>
    [Fact]
    public void EveryTypeChannelPair_HasTemplate()
    {
        var covered = Catalog
            .Select(e => (e.Type, e.Channel))
            .ToHashSet();

        var missing = new List<string>();

        foreach (var (type, channels) in NotificationDispatchOptions.DefaultTypeChannelMatrix)
        {
            foreach (var channel in channels)
            {
                if (!covered.Contains((type, channel)))
                    missing.Add($"{type}/{channel}");
            }
        }

        missing.Should().BeEmpty("mọi ô của ma trận type × kênh phải có template");
    }

    /// <summary>Type nào có trong ma trận cũng phải có nội dung — thiếu là im lặng rơi về hardcode.</summary>
    [Fact]
    public void EveryTypeInMatrix_HasContent()
    {
        var typesInCatalog = Catalog.Select(e => e.Type).ToHashSet();

        NotificationDispatchOptions.DefaultTypeChannelMatrix.Keys
            .Where(t => !typesInCatalog.Contains(t))
            .Should().BeEmpty();
    }

    /// <summary>
    /// 02/08/2026 — hệ thống tiếng Việt only: mỗi ô ma trận đúng MỘT dòng, không còn bản dịch nào.
    /// Trước đây các type hướng Customer sinh thêm bản <c>en-US</c>, làm mỗi ô có 2 dòng.
    /// </summary>
    [Fact]
    public void Catalog_HasExactlyOneEntryPerMatrixCell()
    {
        var matrixCells = NotificationDispatchOptions.DefaultTypeChannelMatrix
            .Sum(kv => kv.Value.Length);

        Catalog.Should().HaveCount(matrixCells,
            "mỗi ô type × kênh đúng một template tiếng Việt — không còn bản dịch");
    }

    /// <summary>
    /// 10/08/2026 — hệ thống chuyển sang tiếng Anh only. Không còn template tiếng Việt nào lọt lại
    /// trong danh mục (đảo ngược từ bản kiểm "no English leftovers" thời tiếng Việt only).
    /// </summary>
    [Fact]
    public void Catalog_HasNoVietnameseLeftovers()
    {
        // Câu tiếng Việt của dự án luôn có ít nhất một ký tự có dấu; bản tiếng Anh thì không.
        const string vietnameseChars =
            "ăâđêôơưàảãáạằẳẵắặầẩẫấậèẻẽéẹềểễếệìỉĩíịòỏõóọồổỗốộờởỡớợùủũúụừửữứựỳỷỹýỵ";

        // Template chuyển tiếp nguyên văn (chỉ gồm placeholder, không có chữ nào của mình) đương
        // nhiên không có dấu — đó không phải sót tiếng Việt. Hiện chỉ `System`/`ChatCreated` thuộc
        // dạng này: nội dung của nó CHÍNH LÀ thông điệp admin/consumer gõ, không có gì để khuôn mẫu hoá.
        static bool ChiGomPlaceholder(string text) =>
            System.Text.RegularExpressions.Regex
                .Replace(text, @"\{\{\{?[^}]*\}?\}\}", string.Empty)
                .Trim()
                .Length == 0;

        var suspects = Catalog
            .Where(e => !ChiGomPlaceholder(e.Title + " " + e.Body))
            .Where(e => (e.Title + e.Body).ToLowerInvariant().Any(vietnameseChars.Contains))
            .Select(e => $"{e.Type}/{e.Channel}")
            .ToList();

        suspects.Should().BeEmpty("mọi template phải là tiếng Anh");
    }

    /// <summary>Cặp (Type, Channel) là khoá unique trong DB — trùng là seeder sẽ nổ.</summary>
    [Fact]
    public void Catalog_HasNoDuplicateKeys()
    {
        Catalog.GroupBy(e => (e.Type, e.Channel))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .Should().BeEmpty();
    }

    [Fact]
    public void EveryTemplate_HasNonEmptyTitleAndBody()
    {
        Catalog.Should().OnlyContain(e =>
            !string.IsNullOrWhiteSpace(e.Title) && !string.IsNullOrWhiteSpace(e.Body));
    }

    /// <summary>SMS tính tiền theo đoạn 160 ký tự — gửi nguyên văn bản email qua SMS là đốt tiền.</summary>
    [Fact]
    public void SmsTemplates_AreCompact()
    {
        var smsEntries = Catalog.Where(e => e.Channel == NotificationChannelEnum.Sms).ToList();

        smsEntries.Should().NotBeEmpty("phải có template SMS — nếu không test này vô nghĩa");
        smsEntries.Should().OnlyContain(e => e.Body.Length <= 300);
    }

    /// <summary>
    /// Cột <c>body_template</c> giới hạn 4000 ký tự và <c>title_template</c> 500 — vượt là
    /// seeder ném lỗi ngay lúc khởi động service.
    /// </summary>
    [Fact]
    public void Templates_FitDatabaseColumnLimits()
    {
        Catalog.Should().OnlyContain(e => e.Title.Length <= 500 && e.Body.Length <= 4000);
    }

    /// <summary>Template hỏng cú pháp chỉ lộ ra khi có sự kiện thật — bắt ngay ở CI.</summary>
    [Fact]
    public void EveryTemplate_CompilesWithHandlebars()
    {
        var renderer = new HandlebarsTemplateRenderer();

        foreach (var entry in Catalog)
        {
            var act = () =>
            {
                renderer.RenderInline(entry.Title, new { });
                renderer.RenderInline(entry.Body, new { });
            };

            act.Should().NotThrow($"{entry.Type}/{entry.Channel} phải compile được");
        }
    }

    /// <summary>Placeholder phải là <c>{{tên}}</c> hợp lệ — sai cú pháp thì render ra chuỗi rỗng âm thầm.</summary>
    [Fact]
    public void Templates_UseWellFormedPlaceholders()
    {
        foreach (var entry in Catalog)
        {
            foreach (var text in new[] { entry.Title, entry.Body })
            {
                // Số ngoặc mở và đóng phải khớp.
                var open = System.Text.RegularExpressions.Regex.Matches(text, @"\{\{").Count;
                var close = System.Text.RegularExpressions.Regex.Matches(text, @"\}\}").Count;

                open.Should().Be(close,
                    $"{entry.Type}/{entry.Channel}: số ngoặc {{{{ và }}}} không khớp");
            }
        }
    }

}
