using System.Text.RegularExpressions;
using FluentAssertions;

namespace BatteryService.UnitTests.Domain;

/// <summary>
/// Hàng rào chống tái phát lớp lỗi <b>"Alert quên set Id"</b> (phát hiện 2026-08-01 khi làm DoD
/// verify của Sprint IoT-2).
///
/// <para><b>Bản chất lỗi:</b> <c>BaseEntity.Id</c> khai là <c>public Guid Id { get; set; }</c> —
/// KHÔNG có giá trị khởi tạo — và <c>Alert.Id</c> KHÔNG được cấu hình <c>ValueGeneratedOnAdd</c>.
/// Vì vậy <c>new Alert { ... }</c> mà bỏ trống <c>Id</c> thì mang <c>Guid.Empty</c>. Tạo 1 alert thì
/// chạy bình thường; tạo <b>từ 2 alert trở lên trong cùng một DbContext</b> là EF ném:
/// <code>
/// The instance of entity type 'Alert' cannot be tracked because another instance
/// with the same key value for {'Id'} is already being tracked.
/// </code></para>
///
/// <para><b>Vì sao khó thấy:</b> lỗi chỉ nổ khi có ≥ 2 alert trong một lượt — site 1 pin, quét ra 1
/// mismatch, 1 calibration hết hạn thì đều qua. Nó đã xảy ra 3 lần ở 3 chỗ khác nhau
/// (<c>MqttBridgeBackgroundService</c>, <c>CrossSourceValidationService</c>,
/// <c>CalibrationExpiryNotificationBackgroundService</c>) nên cần chặn bằng test, không dựa vào trí nhớ.</para>
///
/// <para>Test quét mã nguồn thay vì chạy từng đường — rẻ, và bao được cả những đường mới thêm sau này.</para>
/// </summary>
public class AlertIdAssignmentGuardTests
{
    private static string SrcRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            dir.Should().NotBeNull("phải tìm được gốc repo từ thư mục chạy test");
            return Path.Combine(dir!.FullName, "services", "BatteryService", "src");
        }
    }

    [Fact]
    public void EveryAlertObjectInitializer_SetsIdExplicitly()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(text, @"new (?:Domain\.Entities\.)?Alert\s*\{"))
            {
                var block = ReadBalancedBlock(text, m.Index, m.Index + m.Length);

                // Chỉ xét khối thực sự dựng entity Alert (có AnomalyType) — bỏ DTO/mapper trùng tên.
                if (!block.Contains("AnomalyType"))
                    continue;

                if (!Regex.IsMatch(block, @"\bId\s*="))
                {
                    var line = text[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "mọi `new Alert { ... }` phải set Id tường minh (Id = Guid.NewGuid()). " +
            "Bỏ trống là Guid.Empty, và đường nào tạo >= 2 alert một lượt sẽ ném " +
            "\"another instance with the same key value for {'Id'} is already being tracked\". " +
            "Các chỗ vi phạm: " + string.Join(", ", offenders));
    }

    private static string ReadBalancedBlock(string text, int blockStart, int bodyStart)
    {
        var depth = 1;
        var j = bodyStart;
        while (j < text.Length && depth > 0)
        {
            if (text[j] == '{')
                depth++;
            else if (text[j] == '}')
                depth--;
            j++;
        }
        return text[blockStart..j];
    }
}
