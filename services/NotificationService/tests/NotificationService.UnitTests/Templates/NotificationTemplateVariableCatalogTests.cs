using System.Text.RegularExpressions;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Persistence.Seeders;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// 03/08/2026 — giữ cho <see cref="NotificationTemplateVariables"/> không trôi khỏi thực tế.
///
/// <para>Danh mục biến là hợp đồng giữa consumer (bên ghi <c>PayloadJson</c>) và template (bên đọc
/// <c>{{bien}}</c>). Hợp đồng khai bằng tay thì sớm muộn cũng lệch, mà lệch thì <b>không có gì báo</b>
/// — Handlebars render biến lạ ra rỗng chứ không ném. Ba test dưới đây khép ba đường lệch:</para>
///
/// <list type="number">
///   <item><description>Template seed dùng biến ngoài danh mục.</description></item>
///   <item><description>Danh mục khai khoá mà consumer không hề ghi.</description></item>
///   <item><description>Consumer ghi khoá mà danh mục chưa khai.</description></item>
/// </list>
/// </summary>
public class NotificationTemplateVariableCatalogTests
{
    private static readonly IReadOnlyList<NotificationTemplateCatalog.Entry> Catalog =
        NotificationTemplateCatalog.Build(NotificationDispatchOptions.DefaultTypeChannelMatrix);

    private static readonly string ConsumerDir = ResolveDir(
        "NotificationService.Application", "Consumers");

    /// <summary>
    /// Bản tin gom (digest) cũng dựng payload, nhưng nó là background job chứ không phải consumer —
    /// bốn khoá <c>digest/from/to/notificationIds</c> chỉ xuất hiện ở đây.
    /// </summary>
    private static readonly string BackgroundJobDir = ResolveDir(
        "NotificationService.Infrastructure", "BackgroundJobs");

    private static string ResolveDir(string project, string folder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("SolarBatteryMaintainance.slnx").Length == 0)
            dir = dir.Parent;

        Assert.True(dir is not null, $"Không tìm thấy repo root từ {AppContext.BaseDirectory}");
        var path = Path.Combine(dir!.FullName, "services", "NotificationService", "src", project, folder);
        Assert.True(Directory.Exists(path), $"Không thấy thư mục '{folder}' tại '{path}'");
        return path;
    }

    /// <summary>
    /// Đường lệch 1 — và là test quan trọng nhất file này.
    ///
    /// <para>Trước 03/08/2026 test này sẽ đỏ ở gần như mọi type: bộ template seed soạn theo một hợp
    /// đồng payload tưởng tượng (<c>{{ticketCode}}</c>, <c>{{serialNumber}}</c>, <c>{{customerName}}</c>,
    /// <c>{{slaDeadline}}</c>, <c>{{senderName}}</c>…) trong khi consumer ghi những khoá hoàn toàn
    /// khác. Hệ quả là hàng nghìn thông báo đã gửi đi với chỗ trống ngay giữa câu.</para>
    /// </summary>
    [Fact]
    public void MoiTemplateSeed_ChiDungBienCoThat()
    {
        var viPham = new List<string>();

        foreach (var entry in Catalog)
        {
            var loi = TemplateVariableGuard.FindUnknownVariables(entry.Type, entry.Title, entry.Body);
            if (loi is not null)
                viPham.Add($"{entry.Type}/{entry.Channel}: {loi}");
        }

        viPham.Should().BeEmpty(
            "template seed gọi biến không tồn tại thì render ra rỗng, người nhận đọc phải câu cụt");
    }

    /// <summary>
    /// Đường lệch 2: danh mục khai một khoá mà consumer không hề ghi ⇒ trình soạn gợi ý một biến
    /// vĩnh viễn rỗng, đúng cái bẫy mà file này sinh ra để chặn.
    /// </summary>
    [Fact]
    public void MoiKhoaTrongDanhMuc_DeuXuatHienOMotConsumerNaoDo()
    {
        var nguon = string.Join("\n",
            Directory.GetFiles(ConsumerDir, "*.cs")
                .Concat(Directory.GetFiles(BackgroundJobDir, "*.cs"))
                .Select(File.ReadAllText));

        var thieu = new List<string>();

        foreach (var type in NotificationTemplateVariables.DeclaredTypes)
        {
            foreach (var key in NotificationTemplateVariables.PayloadKeysFor(type))
            {
                // Consumer dựng payload theo hai lối: anonymous object (`code = evt.Code`) và chuỗi
                // nội suy (`\"chatId\":\"{...}\"`). Bắt cả hai.
                var gan = new Regex($@"\b{Regex.Escape(key)}\s*=", RegexOptions.IgnoreCase);
                var chuoi = new Regex($@"\\?""{Regex.Escape(key)}\\?""\s*:", RegexOptions.IgnoreCase);

                if (!gan.IsMatch(nguon) && !chuoi.IsMatch(nguon))
                    thieu.Add($"{type}.{key}");
            }
        }

        thieu.Should().BeEmpty(
            "danh mục khai khoá mà không consumer nào ghi ⇒ trình soạn gợi ý một biến luôn rỗng");
    }

    /// <summary>
    /// Đường lệch 3: consumer thêm khoá mới vào payload mà quên khai vào danh mục ⇒ người soạn không
    /// dùng được biến đó, và tệ hơn: guard sẽ CHẶN OAN nếu họ tự đoán ra đúng tên.
    ///
    /// <para>Chỉ soi những consumer dựng payload bằng anonymous object — dạng chuỗi nội suy quá ít
    /// và quá khác nhau để phân tích tĩnh cho đáng.</para>
    /// </summary>
    [Fact]
    public void MoiKhoaConsumerGhi_DeuDaKhaiTrongDanhMuc()
    {
        var moiKhoaDaKhai = NotificationTemplateVariables.DeclaredTypes
            .SelectMany(NotificationTemplateVariables.PayloadKeysFor)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var khoiPayload = new Regex(
            @"JsonSerializer\.Serialize\(new\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        var dongGan = new Regex(@"^\s*(?<key>[a-z][a-zA-Z0-9]*)\s*=", RegexOptions.Multiline);

        var chuaKhai = new List<string>();

        foreach (var file in Directory.GetFiles(ConsumerDir, "*.cs"))
        {
            var ten = Path.GetFileNameWithoutExtension(file);
            foreach (System.Text.RegularExpressions.Match khoi
                     in khoiPayload.Matches(File.ReadAllText(file)))
            {
                foreach (System.Text.RegularExpressions.Match dong
                         in dongGan.Matches(khoi.Groups["body"].Value))
                {
                    var key = dong.Groups["key"].Value;
                    if (!moiKhoaDaKhai.Contains(key))
                        chuaKhai.Add($"{ten}: {key}");
                }
            }
        }

        chuaKhai.Should().BeEmpty(
            "consumer thêm khoá payload thì phải khai vào NotificationTemplateVariables, "
          + "nếu không người soạn template không dùng được biến đó");
    }

    /// <summary>
    /// Template thông báo pin phải dùng cặp <c>{{anomalyTypeName}}</c>/<c>{{severityName}}</c>, KHÔNG
    /// dùng hai khoá số <c>{{anomalyType}}</c>/<c>{{severity}}</c>.
    ///
    /// <para>Hai khoá số vẫn nằm trong payload (client cần để lọc/so sánh) nên chúng <b>hợp lệ</b> —
    /// bộ kiểm tên biến không hề chặn. Nhưng in ra template thì thành "Loại 4 — mức 3", đúng thứ hệ
    /// thống gửi cho khách suốt thời gian trước 03/08/2026. Không có test này thì chỉ cần một lần
    /// sửa câu chữ vô ý là quay lại nguyên trạng mà chẳng ai hay.</para>
    /// </summary>
    [Fact]
    public void TemplatePin_DungTenChu_KhongDungKhoaSo()
    {
        var loaiPin = new[]
        {
            NotificationTypeEnum.BatteryAnomalyDetected,
            NotificationTypeEnum.BatteryAnomalyWarning,
            NotificationTypeEnum.BatteryAnomalyInfo,
        };

        var viPham = Catalog
            .Where(e => loaiPin.Contains(e.Type))
            .SelectMany(e => TemplateVariableGuard
                .ExtractVariables(e.Title)
                .Concat(TemplateVariableGuard.ExtractVariables(e.Body))
                .Where(v => v is "anomalyType" or "severity")
                .Select(v => $"{e.Type}/{e.Channel}: {{{{{v}}}}}"))
            .ToList();

        viPham.Should().BeEmpty(
            "hai khoá đó là SỐ nguyên — in thẳng ra thì người nhận đọc phải \"Loại 4 — mức 3\"; "
          + "phải dùng anomalyTypeName/severityName");
    }

    /// <summary>
    /// Template của <c>System</c> phải là <b>chuyển tiếp nguyên văn</b> — cả tiêu đề lẫn thân chỉ
    /// gồm biến builtin.
    ///
    /// <para>Từ 03/08/2026 kênh InApp ghi ngược nội dung đã render vào dòng notification. Nếu template
    /// System có tiêu đề cố định (bản trước là "Thông báo hệ thống") thì nó sẽ <b>đè mất tiêu đề admin
    /// vừa gõ</b> lúc gửi hàng loạt — người nhận mở feed chỉ thấy dòng chữ cố định thay vì
    /// "Bảo trì hệ thống 22:00". Với System thì nội dung CHÍNH LÀ thông điệp, không có gì để khuôn
    /// mẫu hoá.</para>
    /// </summary>
    [Fact]
    public void TemplateSystem_PhaiChuyenTiepNguyenVan()
    {
        var system = Catalog.Where(e => e.Type == NotificationTypeEnum.System).ToList();
        system.Should().NotBeEmpty();

        foreach (var e in system)
        {
            e.Title.Trim().Should().Be("{{Title}}",
                "tiêu đề cố định sẽ đè mất tiêu đề admin nhập lúc gửi hàng loạt");
            e.Body.Trim().Should().Be("{{Body}}");
        }
    }

    /// <summary>
    /// Hai type khác nhau mà chung một số thì <c>ToString()</c> chỉ trả về một tên, và khoá duy nhất
    /// <c>(type, channel)</c> của bảng template khiến chúng tranh nhau một ô — không thể có template
    /// riêng. <c>TicketMerged</c> và <c>ChatEscalatedToAdmin</c> từng cùng mang giá trị 27.
    /// </summary>
    [Fact]
    public void KhongCoHaiTypeNaoTrungGiaTri()
    {
        var trung = Enum.GetValues<NotificationTypeEnum>()
            .Cast<int>()
            .GroupBy(v => v)
            .Where(g => g.Count() > 1)
            .Select(g => $"giá trị {g.Key} bị {g.Count()} type dùng chung")
            .ToList();

        trung.Should().BeEmpty();
    }

    /// <summary>
    /// Danh mục biến phải phủ mọi type có mặt trong ma trận kênh — thiếu thì template của type đó
    /// chỉ dùng được biến builtin, mà guard lại chặn mọi biến payload người soạn gõ vào.
    /// </summary>
    [Fact]
    public void MoiTypeCoTemplate_DeuCoMucTrongDanhMuc_HoacCoLyDoRoRang()
    {
        // Cố ý không khai: consumer của type này không ghi payload nào.
        // (AdminInvite từng nằm đây, đã bị gỡ hẳn khỏi enum 03/08/2026 vì không có producer.)
        var mienTru = new HashSet<NotificationTypeEnum>
        {
            NotificationTypeEnum.EnvironmentalIncidentResolved,
        };

        var thieu = Catalog
            .Select(e => e.Type)
            .Distinct()
            .Where(t => !mienTru.Contains(t))
            .Where(t => NotificationTemplateVariables.PayloadKeysFor(t).Count == 0)
            .ToList();

        thieu.Should().BeEmpty("type có template mà không khai biến thì người soạn không có gì để dùng");
    }
}
