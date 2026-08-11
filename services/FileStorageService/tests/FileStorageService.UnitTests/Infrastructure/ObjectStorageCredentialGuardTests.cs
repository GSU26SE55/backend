using FileStorageService.Infrastructure.Options;
using FluentAssertions;
using Xunit;

namespace FileStorageService.UnitTests.Infrastructure;

/// <summary>
/// GH-788 — production MinIO chạy bằng <c>minioadmin/minioadmin</c>.
///
/// <para>
/// Đo được trên chính repo: <c>docker-compose.prod.yml</c> dùng
/// <c>${ObjectStorage__AccessKey:-minioadmin}</c>, <c>env.prod.example</c> ghi thẳng
/// <c>minioadmin</c>, và — sâu hơn cả hai — <see cref="ObjectStorageOptions"/> đặt <c>minioadmin</c>
/// làm <b>giá trị mặc định trong mã</b>. Nghĩa là vá compose vẫn chưa đủ: bất kỳ đường triển khai
/// nào khác (chạy tay, môi trường mới, cụm dựng để thử) đều rơi lại về credential đoán được.
/// </para>
/// <para>
/// Console MinIO đăng nhập bằng chính root credential ⇒ đoán trúng là toàn quyền trên mọi bucket:
/// đọc, sửa, xoá đính kèm ticket, ảnh bảo trì, tài liệu bảo hành.
/// </para>
/// </summary>
public class ObjectStorageCredentialGuardTests
{
    private static ObjectStorageOptions Opt(string accessKey, string secretKey) => new()
    {
        AccessKey = accessKey,
        SecretKey = secretKey,
    };

    /// <summary>Giá trị hợp lệ dùng chung cho các ca chỉ quan tâm một phía.</summary>
    private const string GoodAccessKey = "a1b2c3d4e5f60718";
    private const string GoodSecretKey = "s3cr3t-du-dai-va-ngau-nhien-32ky";

    [Fact]
    public void Options_NoLongerCarryDefaultCredentials()
    {
        // ĐÂY là gốc rễ. Còn mặc định trong mã thì mọi hàng rào phía ngoài chỉ là lớp sơn.
        var fresh = new ObjectStorageOptions();

        fresh.AccessKey.Should().BeEmpty("mặc định trong mã làm cấu hình thiếu trở nên vô hình");
        fresh.SecretKey.Should().BeEmpty();
    }

    [Fact]
    public void MissingCredentials_AreRejected_EvenLocally()
    {
        // Không còn mặc định để rơi về, nên thiếu là hỏng ở mọi nơi — báo sớm còn hơn để lỗi hiện ra
        // dưới dạng "AccessDenied" lúc upload.
        var errors = ObjectStorageCredentialGuard.Validate(Opt("", ""), isLocalEnvironment: true);

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("AccessKey"));
        errors.Should().Contain(e => e.Contains("SecretKey"));
    }

    [Theory]
    [InlineData("minioadmin")]
    [InlineData("MinioAdmin")]      // hoa thường không cứu được: vẫn nằm trong mọi wordlist
    [InlineData("  minioadmin  ")]  // khoảng trắng thừa khi copy từ tài liệu
    [InlineData("admin")]
    [InlineData("CHANGE_ME")]
    [InlineData("THAY-BANG-GIA-TRI-SINH-NGAU-NHIEN")]
    public void DefaultLikeCredentials_AreRejectedOutsideLocal(string weak)
    {
        var errors = ObjectStorageCredentialGuard.Validate(Opt(weak, GoodSecretKey), isLocalEnvironment: false);

        errors.Should().ContainSingle().Which.Should().Contain("AccessKey");
    }

    [Fact]
    public void WeakSecretKey_IsRejectedOutsideLocal()
    {
        var errors = ObjectStorageCredentialGuard.Validate(Opt(GoodAccessKey, "minioadmin"), isLocalEnvironment: false);

        errors.Should().ContainSingle().Which.Should().Contain("SecretKey");
    }

    [Fact]
    public void ShortSecretKey_IsRejectedOutsideLocal()
    {
        // Không nằm trong danh sách cấm nhưng ngắn tới mức dò được — chặn danh sách mà không chặn độ
        // dài thì chỉ cần đổi thành "minio1" là lọt.
        var errors = ObjectStorageCredentialGuard.Validate(Opt(GoodAccessKey, "minio1"), isLocalEnvironment: false);

        errors.Should().ContainSingle().Which.Should().Contain("too short");
    }

    [Fact]
    public void LocalEnvironment_StillAllowsMinioadmin()
    {
        // docker-compose.yml và .env của máy cá nhân dùng minioadmin. Siết ở Development chỉ khiến
        // mọi người tìm cách tắt kiểm tra đi — và lúc đó production mất luôn hàng rào.
        ObjectStorageCredentialGuard.Validate(Opt("minioadmin", "minioadmin"), isLocalEnvironment: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void StrongCredentials_PassOutsideLocal()
    {
        ObjectStorageCredentialGuard.Validate(Opt(GoodAccessKey, GoodSecretKey), isLocalEnvironment: false)
            .Should().BeEmpty();
    }

    [Fact]
    public void AllProblems_AreReportedTogether()
    {
        // Trả từng lỗi một sẽ bắt người triển khai chạy lại nhiều lượt, mỗi lượt lộ thêm một lỗi.
        var errors = ObjectStorageCredentialGuard.Validate(Opt("admin", "admin"), isLocalEnvironment: false);

        errors.Should().HaveCount(2);
    }

    [Fact]
    public void ThrowIfInvalid_NamesEveryProblem_SoOneFixRoundIsEnough()
    {
        var act = () => ObjectStorageCredentialGuard.ThrowIfInvalid(Opt("minioadmin", "x"), isLocalEnvironment: false);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("AccessKey").And.Contain("SecretKey").And.Contain("GH-788");
    }

    [Fact]
    public void ThrowIfInvalid_StaysQuiet_WhenConfigurationIsSound()
    {
        var act = () => ObjectStorageCredentialGuard.ThrowIfInvalid(Opt(GoodAccessKey, GoodSecretKey), isLocalEnvironment: false);

        act.Should().NotThrow();
    }

    // ───────────────── Tên môi trường quyết định nới hay siết ─────────────────
    //
    // Bản đầu của phép kiểm này dựng cờ bằng `IsDevelopment()`. Đo được lúc chạy thật:
    // docker-compose của repo đặt ASPNETCORE_ENVIRONMENT=Docker ⇒ cờ thành false ⇒
    // `filestorageservice` vào crash-loop (exit 133) với đúng credential minioadmin mà stack dev
    // vẫn luôn dùng. Không test nào lúc đó chạm tới bước dựng cờ, nên mọi thứ vẫn xanh.

    [Theory]
    [InlineData("Development")]
    [InlineData("Docker")]
    [InlineData("docker")]      // ASPNETCORE_ENVIRONMENT không phân biệt hoa thường
    [InlineData("DOCKER")]
    public void LocalEnvironmentNames_AreRecognised(string environmentName)
    {
        ObjectStorageCredentialGuard.IsLocalEnvironment(environmentName).Should().BeTrue(
            $"'{environmentName}' là môi trường cục bộ — chặn ở đó thì không ai chạy được stack dev");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(null)]
    public void NonLocalEnvironments_StayStrict(string? environmentName)
    {
        ObjectStorageCredentialGuard.IsLocalEnvironment(environmentName).Should().BeFalse(
            "ngoài môi trường cục bộ thì credential mặc định phải bị chặn");
    }

    [Fact]
    public void DockerEnvironment_StartsWithTheCredentialsTheDevStackActuallyUses()
    {
        // Phép kiểm đầu-cuối của chính khe hở đã xảy ra: giá trị trong `.env.Docker`, dưới tên môi
        // trường mà compose đặt, phải khởi động được.
        var act = () => ObjectStorageCredentialGuard.ThrowIfInvalid(
            Opt("minioadmin", "minioadmin"),
            ObjectStorageCredentialGuard.IsLocalEnvironment("Docker"));

        act.Should().NotThrow("stack docker-compose dev phải lên được y như trước GH-788");
    }

    [Fact]
    public void ProductionEnvironment_StillRefusesTheSameCredentials()
    {
        // Nới cho Docker KHÔNG được làm thủng mục đích ban đầu của GH-788.
        var act = () => ObjectStorageCredentialGuard.ThrowIfInvalid(
            Opt("minioadmin", "minioadmin"),
            ObjectStorageCredentialGuard.IsLocalEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GH-788*");
    }
}
