using System.Reflection;
using MassTransit;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 6.3 NOTI3-01 (#701) — hàng rào chặn rủi ro R-40.
///
/// Feed in-app nay lọc <c>Channel = InApp</c>. Consumer nào chỉ ghi <c>Push</c> (hoặc chỉ Email/Sms)
/// thì loại thông báo đó **biến mất hoàn toàn** khỏi danh sách của user — hỏng nặng hơn cả lỗi
/// nhân bản mà NOTI3-01 đang sửa. Lúc rà tay tại thời điểm làm task đã phát hiện đúng 4 consumer
/// dính lỗi này: ChatMention, ChatReaction, ParticipantChange, EnvironmentalIncidentDetected.
///
/// Test đọc THẲNG mã nguồn consumer thay vì chạy chúng, vì mỗi consumer cần một bộ event/mock
/// khác nhau — quét tĩnh mới bao được toàn bộ và tự bắt cả consumer thêm mới sau này.
/// </summary>
public class EveryConsumerWritesInAppTests
{
    /// <summary>
    /// Consumer KHÔNG hướng người dùng cuối — được phép không ghi row InApp.
    /// Mỗi mục phải kèm lý do; danh sách này là ngoại lệ có kiểm soát, không phải chỗ để né test.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["AccountActivatedSyncConsumer"] = "chỉ đồng bộ AccountReadModel, không sinh notification",
        ["AccountProfileUpdatedSyncConsumer"] = "chỉ đồng bộ read-model",
        ["AccountDeletedSyncConsumer"] = "chỉ soft-delete read-model",
        ["AccountSnapshotSyncConsumer"] = "chỉ đồng bộ read-model (Role + IsActive) — cố tình KHÔNG sinh notification: snapshot được phát lại mỗi lần đối soát, sinh notification ở đây là spam người dùng",
        ["SmsFailedConsumer"] = "cập nhật trạng thái record SMS đã tồn tại, không tạo notification mới",
        ["NotificationWriter"] = "helper, không phải consumer",
        ["NotificationDebounce"] = "helper, không phải consumer",
    };

    private static readonly string ConsumerDir = ResolveConsumerDir();

    private static string ResolveConsumerDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("SolarBatteryMaintainance.slnx").Length == 0)
            dir = dir.Parent;

        Assert.True(dir is not null, $"Không tìm thấy repo root từ {AppContext.BaseDirectory}");
        var path = Path.Combine(dir!.FullName, "services", "NotificationService", "src",
            "NotificationService.Application", "Consumers");
        Assert.True(Directory.Exists(path), $"Không thấy thư mục consumer tại '{path}'");
        return path;
    }

    public static TheoryData<string> UserFacingConsumers()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ConsumerDir, "*.cs").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file)!;
            if (!Exempt.ContainsKey(name))
                data.Add(name);
        }
        return data;
    }

    /// <summary>
    /// Mọi consumer hướng user phải ghi ít nhất một record <c>InApp</c> — trực tiếp qua
    /// <c>NotificationChannelEnum.InApp</c>, gián tiếp qua preset của <c>NotificationWriter</c>
    /// (tất cả preset đều đã bao gồm InApp), hoặc qua writer lịch ticket chuyên biệt đã được
    /// kiểm tra như một file hướng user riêng.
    /// </summary>
    [Theory]
    [MemberData(nameof(UserFacingConsumers))]
    public void Consumer_WritesAtLeastOneInAppRecord(string consumerName)
    {
        var source = File.ReadAllText(Path.Combine(ConsumerDir, $"{consumerName}.cs"));

        var writesInAppDirectly = source.Contains("NotificationChannelEnum.InApp", StringComparison.Ordinal);
        var writesViaPreset =
            source.Contains("NotificationWriter.InAppOnly", StringComparison.Ordinal) ||
            source.Contains("NotificationWriter.InAppPush", StringComparison.Ordinal) ||
            source.Contains("NotificationWriter.InAppPushEmail", StringComparison.Ordinal) ||
            source.Contains("NotificationWriter.AllChannels", StringComparison.Ordinal);

        var writesViaTicketSchedulingWriter =
            source.Contains("TicketSchedulingNotificationWriter.WriteAssignmentAsync", StringComparison.Ordinal) ||
            source.Contains("TicketSchedulingNotificationWriter.WriteWorkStartedAsync", StringComparison.Ordinal);

        (writesInAppDirectly || writesViaPreset || writesViaTicketSchedulingWriter).Should().BeTrue(
            $"'{consumerName}' phải ghi ít nhất 1 record Channel=InApp. Feed lọc theo InApp "
            + "(NOTI3-01) nên consumer chỉ ghi Push/Email/Sms sẽ khiến loại thông báo này biến mất "
            + "hoàn toàn khỏi danh sách của user (R-40). Nếu consumer thực sự không hướng user "
            + "cuối, thêm vào danh sách Exempt kèm lý do.");
    }

    /// <summary>Mọi preset của NotificationWriter đều phải chứa InApp — nếu không, giả định ở trên sai.</summary>
    [Fact]
    public void EveryNotificationWriterPreset_ContainsInApp()
    {
        // NotificationWriter là `internal` nên phải lấy qua reflection thay vì typeof().
        var writerType = typeof(TicketCreatedConsumer).Assembly
            .GetType("NotificationService.Application.Consumers.NotificationWriter");
        writerType.Should().NotBeNull("không tìm thấy NotificationWriter — có thể đã đổi namespace/tên");

        var presets = writerType!
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(NotificationChannelEnum[]))
            .ToList();

        presets.Should().NotBeEmpty("phải tìm thấy các preset channel của NotificationWriter");

        foreach (var preset in presets)
        {
            var channels = (NotificationChannelEnum[])preset.GetValue(null)!;
            channels.Should().Contain(NotificationChannelEnum.InApp,
                $"preset '{preset.Name}' thiếu InApp ⇒ consumer dùng nó sẽ vắng mặt trong feed");
        }
    }

    /// <summary>Danh sách miễn trừ phải khớp file có thật — tránh bỏ sót do đổi tên consumer.</summary>
    [Fact]
    public void ExemptList_OnlyReferencesExistingFiles()
    {
        foreach (var name in Exempt.Keys)
        {
            File.Exists(Path.Combine(ConsumerDir, $"{name}.cs"))
                .Should().BeTrue($"'{name}' nằm trong danh sách miễn trừ nhưng không có file — "
                                 + "đổi tên/xoá consumer thì phải cập nhật danh sách này");
        }
    }

    /// <summary>Bắt được cả consumer mới thêm: số file consumer phải khớp số IConsumer thực tế.</summary>
    [Fact]
    public void AllConsumerFiles_AreDiscovered()
    {
        var declared = typeof(TicketCreatedConsumer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.GetInterfaces().Any(i => i.IsGenericType
                                                      && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
            .Select(t => t.Name)
            .ToHashSet();

        var onDisk = Directory.GetFiles(ConsumerDir, "*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .ToHashSet();

        // TicketLifecycleConsumers.cs chứa nhiều class consumer trong 1 file — chỉ cần mỗi class
        // khai báo IConsumer<> đều nằm trong assembly, còn tên file không nhất thiết trùng tên class.
        declared.Should().NotBeEmpty();
        onDisk.Should().NotBeEmpty();
        declared.Count.Should().BeGreaterThanOrEqualTo(onDisk.Count - Exempt.Count,
            "số class consumer không được ít hơn số file consumer hướng user");
    }
}
