using System.Security.Cryptography;
using System.Text;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-784 — hash mật khẩu MQTT sinh ra KHÔNG phải định dạng Mosquitto đọc được.
///
/// <para>
/// Bản cũ xuất <c>PBKDF2$sha256${iter}${salt}${hash}</c> (SHA256, 32 byte) và tự chú thích là
/// "Mosquitto-compatible". Mosquitto KHÔNG hiểu tiền tố đó — nó chỉ đọc
/// <c>$7$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c> với PBKDF2-HMAC-SHA512 output 64 byte.
/// Đối chiếu bản ghi thật do <c>mosquitto_passwd</c> sinh trong
/// <c>infra/mqtt/mosquitto/passwd</c>: <c>backend-bridge:$7$101$&lt;12B salt&gt;$&lt;64B hash&gt;</c>.
/// </para>
/// <para>
/// Đây là lỗi SÂU HƠN issue mô tả: kể cả đồng bộ file passwd hoàn hảo, mọi credential thiết bị vẫn
/// bị broker từ chối — sai từ gốc chứ không phải sai ở khâu đồng bộ.
/// </para>
/// </summary>
public class MosquittoPasswordFormatTests
{
    private static IotApiKeyService Service()
        => new(new MockUnitOfWorkBuilder().Build());

    [Fact]
    public void GeneratedHash_UsesMosquittoDollarSevenFormat()
    {
        var cred = Service().GenerateMqttCredential("E2E-IOT-230605");

        // `$7$` = PBKDF2-HMAC-SHA512 theo cách Mosquitto đánh số thuật toán.
        cred.PasswordHash.Should().StartWith("$7$");
        cred.PasswordHash.Should().NotStartWith("PBKDF2$",
            "tiền tố PBKDF2$ là định dạng tự đặt — Mosquitto không đọc được");
    }

    [Fact]
    public void GeneratedHash_HasFourDollarSeparatedParts_LikeMosquittoPasswd()
    {
        var cred = Service().GenerateMqttCredential("dev-001");

        // "" | "7" | iterations | salt | hash  (chuỗi bắt đầu bằng '$' nên phần tử đầu rỗng)
        var parts = cred.PasswordHash.Split('$');
        parts.Should().HaveCount(5);
        parts[0].Should().BeEmpty();
        parts[1].Should().Be("7");
        int.TryParse(parts[2], out var iterations).Should().BeTrue();
        iterations.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeneratedHash_IsSha512SizedAndSaltedLikeTheRealFile()
    {
        var cred = Service().GenerateMqttCredential("dev-002");
        var parts = cred.PasswordHash.Split('$');

        Convert.FromBase64String(parts[4]).Should().HaveCount(64,
            "Mosquitto `$7$` là SHA512 — 32 byte (SHA256) sẽ bị từ chối");
        Convert.FromBase64String(parts[3]).Should().HaveCount(12,
            "khớp độ dài salt của bản ghi mosquitto_passwd thật");
    }

    /// <summary>
    /// Kiểm chứng THẬT SỰ: tái tạo đúng phép tính Mosquitto dùng để xác minh mật khẩu, rồi so với
    /// hash đã lưu. Chỉ kiểm hình dạng chuỗi thì một hash sai thuật toán vẫn qua được.
    /// </summary>
    [Fact]
    public void StoredHash_VerifiesAgainstTheRawPassword_TheWayMosquittoWouldl()
    {
        var cred = Service().GenerateMqttCredential("dev-003");
        var parts = cred.PasswordHash.Split('$');
        var iterations = int.Parse(parts[2]);
        var salt = Convert.FromBase64String(parts[3]);
        var expected = Convert.FromBase64String(parts[4]);

        var recomputed = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(cred.RawPassword),
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            expected.Length);

        recomputed.Should().Equal(expected);
    }

    [Fact]
    public void StoredHash_DoesNotVerifyAgainstAWrongPassword()
    {
        // Chống test vô nghĩa: nếu phép so trên đúng với MỌI mật khẩu thì nó chẳng chứng minh gì.
        var cred = Service().GenerateMqttCredential("dev-004");
        var parts = cred.PasswordHash.Split('$');

        var recomputed = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(cred.RawPassword + "x"),
            Convert.FromBase64String(parts[3]),
            int.Parse(parts[2]),
            HashAlgorithmName.SHA512,
            64);

        recomputed.Should().NotEqual(Convert.FromBase64String(parts[4]));
    }

    [Fact]
    public void StoredHash_IsNotSha256_WhichWasTheOldBug()
    {
        // Ghim đúng lỗi cũ: cùng salt/iterations nhưng SHA256 phải cho kết quả KHÁC.
        var cred = Service().GenerateMqttCredential("dev-005");
        var parts = cred.PasswordHash.Split('$');

        var sha256 = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(cred.RawPassword),
            Convert.FromBase64String(parts[3]),
            int.Parse(parts[2]),
            HashAlgorithmName.SHA256,
            32);

        sha256.Should().NotEqual(Convert.FromBase64String(parts[4]));
    }

    [Fact]
    public void Username_IsLowercase_MatchingTheAclPattern()
    {
        Service().GenerateMqttCredential("  E2E-IOT-230605  ").Username.Should().Be("e2e-iot-230605");
    }
}
