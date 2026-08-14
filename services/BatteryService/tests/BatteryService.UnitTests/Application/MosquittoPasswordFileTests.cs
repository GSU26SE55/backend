using BatteryService.Application.Mqtt;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-784 — soạn file <c>passwd</c> để broker biết credential của thiết bị.
///
/// <para>
/// Rủi ro lớn nhất của việc này KHÔNG phải thiếu thiết bị, mà là <b>xoá mất
/// <c>backend-bridge</c></b> — chính tài khoản BatteryService dùng để nối vào broker. Ghi đè cả
/// file bằng danh sách thiết bị là cầu nối tự khoá mình ra ngoài và toàn bộ telemetry MQTT chết theo,
/// trong khi log chỉ báo "not authorised" ở phía backend.
/// </para>
/// </summary>
public class MosquittoPasswordFileTests
{
    private const string BridgeLine =
        "backend-bridge:$7$101$uCMww8d0vIaVeM8v$uBKXw6H96ImJogsA/38iz1zvY7cwiHE4j8416rXB5ASOtAEcFyX3Tlv9hbCjut66eInAOk/3WrCs7VrvkMFTQA==";

    private static MosquittoCredential Dev(string user, string hash = "$7$10000$c2FsdA==$aGFzaA==")
        => new(user, hash);

    [Fact]
    public void Compose_PreservesTheBridgeAccount()
    {
        var result = MosquittoPasswordFile.Compose(BridgeLine + "\n", new[] { Dev("dev-001") });

        result.Should().Contain(BridgeLine, "xoá backend-bridge là cầu nối tự khoá mình ra ngoài");
        result.Should().Contain("dev-001:");
    }

    [Fact]
    public void Compose_ReplacesAnExistingDeviceLine_WithoutDuplicating()
    {
        // Xoay khoá: dòng cũ phải BIẾN MẤT, không được để hai dòng cùng username (Mosquitto lấy
        // dòng đầu ⇒ khoá mới vừa cấp sẽ không có tác dụng).
        var existing = BridgeLine + "\ndev-001:$7$10000$b2xk$b2xkaGFzaA==\n";

        var result = MosquittoPasswordFile.Compose(existing, new[] { Dev("dev-001", "$7$10000$bmV3$bmV3aGFzaA==") });

        result.Split('\n').Count(l => l.StartsWith("dev-001:")).Should().Be(1);
        result.Should().Contain("dev-001:$7$10000$bmV3$bmV3aGFzaA==");
        result.Should().NotContain("b2xkaGFzaA==");
    }

    [Fact]
    public void Compose_RemovesDeviceLinesThatAreNoLongerProvisioned()
    {
        // Thiết bị bị thu hồi phải MẤT quyền. Giữ lại dòng cũ nghĩa là revoke chỉ có tác dụng trên
        // giấy tờ, còn thiết bị vẫn publish được.
        // Thiết bị cũ nằm TRONG vùng mốc — đúng kịch bản thật: chính service đã ghi nó ở đó.
        var existing = BridgeLine + "\n"
            + MosquittoPasswordFile.BeginMarker + "\n"
            + "dev-old:$7$10000$eA==$eQ==\n"
            + MosquittoPasswordFile.EndMarker + "\n";

        var result = MosquittoPasswordFile.Compose(existing, new[] { Dev("dev-new") });

        result.Should().NotContain("dev-old");
        result.Should().Contain("dev-new:");
        result.Should().Contain(BridgeLine);
    }

    [Fact]
    public void Compose_KeepsOperatorComments()
    {
        var existing = "# do ops them tay 2026-08-04\n" + BridgeLine + "\n";

        var result = MosquittoPasswordFile.Compose(existing, new[] { Dev("dev-001") });

        result.Should().Contain("# do ops them tay 2026-08-04",
            "ghi chú của người vận hành là ngữ cảnh, xoá đi không được gì");
    }

    [Fact]
    public void Compose_SkipsDevicesWithoutCredential()
    {
        // Thiết bị chưa cấp khoá ⇒ không ghi dòng hỏng vào file; Mosquitto 2.0 từ chối nạp cả file
        // nếu gặp bản ghi sai định dạng, tức một thiết bị lỗi làm chết quyền của TẤT CẢ.
        var result = MosquittoPasswordFile.Compose(BridgeLine + "\n", new[]
        {
            Dev("dev-ok"),
            new MosquittoCredential("dev-no-hash", string.Empty),
            new MosquittoCredential(string.Empty, "$7$1$a$b"),
        });

        result.Should().Contain("dev-ok:");
        result.Should().NotContain("dev-no-hash");
        // 2 dòng nội dung (bridge + dev-ok) + 2 dòng mốc.
        result.Split('\n').Where(l => l.Length > 0).Should().HaveCount(4);
    }

    [Fact]
    public void Compose_EndsWithNewline()
    {
        // Thiếu newline cuối thì Mosquitto có thể bỏ qua dòng chót — thiết bị cuối cùng mất quyền
        // một cách hoàn toàn ngẫu nhiên theo thứ tự sắp xếp.
        MosquittoPasswordFile.Compose(BridgeLine + "\n", new[] { Dev("dev-001") })
            .Should().EndWith("\n");
    }

    [Fact]
    public void Compose_OnEmptyFile_StillWritesDevices()
    {
        var result = MosquittoPasswordFile.Compose(null, new[] { Dev("dev-001") });

        result.Should().Contain("dev-001:$7$10000$c2FsdA==$aGFzaA==");
        result.Should().Contain(MosquittoPasswordFile.BeginMarker);
        result.Should().Contain(MosquittoPasswordFile.EndMarker);
    }

    [Fact]
    public void Compose_IsIdempotent_WhenRunTwiceOnItsOwnOutput()
    {
        // Vòng quét chạy lặp lại trên chính file mình vừa ghi. Không lũy đẳng thì mỗi vòng lại sinh
        // một vùng mốc mới chồng lên nhau và file phình mãi.
        var devices = new[] { Dev("dev-001"), Dev("dev-002") };
        var once = MosquittoPasswordFile.Compose(BridgeLine + "\n", devices);
        var twice = MosquittoPasswordFile.Compose(once, devices);

        twice.Should().Be(once);
        twice.Split('\n').Count(l => l == MosquittoPasswordFile.BeginMarker).Should().Be(1);
    }

    [Fact]
    public void Compose_WithNoDevices_LeavesTheFileUntouchedApartFromNormalisation()
    {
        var result = MosquittoPasswordFile.Compose(BridgeLine + "\n", Array.Empty<MosquittoCredential>());

        result.Should().Be(BridgeLine + "\n");
    }

    [Fact]
    public void Compose_IsDeterministic_SoUnchangedStateDoesNotRewriteTheFile()
    {
        // Ghi lại file là kéo theo một lần reload broker. Thứ tự không ổn định sẽ khiến mỗi vòng
        // quét đều "thấy khác" và bắt broker nạp lại vô cớ.
        var devices = new[] { Dev("dev-c"), Dev("dev-a"), Dev("dev-b") };
        var shuffled = new[] { Dev("dev-b"), Dev("dev-c"), Dev("dev-a") };

        MosquittoPasswordFile.Compose(BridgeLine + "\n", devices)
            .Should().Be(MosquittoPasswordFile.Compose(BridgeLine + "\n", shuffled));
    }
}
