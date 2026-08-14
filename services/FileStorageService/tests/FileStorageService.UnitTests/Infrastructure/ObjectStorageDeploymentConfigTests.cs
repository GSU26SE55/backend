using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FileStorageService.UnitTests.Infrastructure;

/// <summary>
/// GH-788 — bản sửa nằm trong file triển khai, nên bằng chứng cũng phải đọc chính file đó.
///
/// <para>
/// Ba khiếm khuyết ở production, đo trực tiếp trên repo:
/// credential mặc định <c>minioadmin</c>, console quản trị mở ra Internet, và
/// <c>mc anonymous set download</c> biến cả bucket thành công khai.
/// Đính kèm ticket, ảnh bảo trì, tài liệu bảo hành nằm chung bucket đó — ai biết object key là tải
/// được, bỏ qua toàn bộ phân quyền của FileStorageService.
/// </para>
/// <para>
/// Các khẳng định dưới đây soi <b>đúng dòng</b> chứ không quét cả file: chú thích trong file có nhắc
/// lại tên lệnh cũ để giải thích vì sao bỏ, quét cả file sẽ bắt nhầm chính lời giải thích đó.
/// </para>
/// </summary>
public class ObjectStorageDeploymentConfigTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Không tìm thấy gốc repo từ " + AppContext.BaseDirectory);
            return dir!.FullName;
        }
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray());
        File.Exists(path).Should().BeTrue($"thiếu file triển khai {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Lấy khối YAML của một service trong compose (tới service kế tiếp cùng mức thụt).</summary>
    private static string ComposeServiceBlock(string yaml, string serviceName)
    {
        var block = Regex.Match(
            yaml, $@"^  {Regex.Escape(serviceName)}:\r?\n(?:.*\r?\n)*?(?=^  \S)", RegexOptions.Multiline).Value;
        block.Should().NotBeEmpty($"không tìm thấy service '{serviceName}' trong compose");
        return block;
    }

    /// <summary>Các dòng thực thi — bỏ dòng trống và dòng chú thích.</summary>
    private static IEnumerable<string> CodeLines(string text)
        => text.Split('\n')
               .Select(l => l.TrimEnd('\r'))
               .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'));

    // ───────────────────────────────────────────────── docker-compose.prod.yml

    [Fact]
    public void ProdCompose_HasNoDefaultCredentialFallback()
    {
        var minio = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "minio");

        // `${VAR:-minioadmin}` là cú pháp "thiếu thì dùng mặc định" — chính nó khiến triển khai
        // thiếu secret vẫn chạy được, và chạy bằng credential ai cũng đoán ra.
        CodeLines(minio).Should().NotContain(l => l.Contains(":-minioadmin"));
    }

    [Fact]
    public void ProdCompose_FailsFast_WhenSecretMissing()
    {
        // Tiêu chí nghiệm thu "Deployment fail-fast khi thiếu secret mạnh": `${VAR:?message}` làm
        // docker compose DỪNG ngay. Không có nó thì cùng lắm là cảnh báo — mà cảnh báo lúc deploy
        // thì không ai đọc.
        var minio = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "minio");

        minio.Should().Contain("${ObjectStorage__AccessKey:?");
        minio.Should().Contain("${ObjectStorage__SecretKey:?");
    }

    [Fact]
    public void ProdCompose_KeepsConsoleOffThePublicInterface()
    {
        var minio = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "minio");

        // Chỉ xét dòng trong `ports:` (dạng `- "…"`). Lọc theo ":9001" đơn thuần sẽ bắt nhầm
        // `--console-address ":9001"` ở dòng command — cùng chuỗi, khác hoàn toàn ý nghĩa.
        var consolePort = CodeLines(minio)
            .FirstOrDefault(l => l.TrimStart().StartsWith("- \"", StringComparison.Ordinal)
                                 && l.Contains(":9001"));
        consolePort.Should().NotBeNull("khối minio phải publish cổng console");
        consolePort!.Should().Contain("127.0.0.1:",
            "console đăng nhập bằng root credential — mở ra Internet là mở toàn quyền mọi bucket");
    }

    [Fact]
    public void ProdCompose_StillPublishesS3Api_SoPresignedUrlsWork()
    {
        // Chiều ngược lại của khẳng định trên: siết quá tay mà đóng luôn cổng S3 thì presigned URL
        // trở thành link chết — biến lỗi rò dữ liệu thành lỗi không tải được file.
        var minio = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "minio");

        CodeLines(minio).Should().Contain(l => l.Contains("\"9090:9000\""));
    }

    [Fact]
    public void ProdCompose_DoesNotMakeBucketAnonymouslyReadable()
    {
        var init = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "minio-init");

        CodeLines(init).Should().NotContain(l => l.Contains("anonymous set download"));
        init.Should().Contain("anonymous set none",
            "đặt 'none' tường minh để lần chạy sau THU HỒI chính sách public đã lỡ áp trên cụm cũ");
    }

    [Fact]
    public void ProdCompose_ConfiguresBrowserReachableHostForPresignedUrls()
    {
        // Đây là nguyên nhân khiến production phải mở bucket anonymous: chữ ký S3 phủ cả Host, mà
        // compose prod không đặt PublicServiceUrl nên URL bị ký cho `minio:9000` — tên chỉ phân giải
        // được bên trong mạng container. Vá bucket mà bỏ qua chỗ này là làm hỏng đường tải file.
        // dev đặt trong docker-compose.yml, k8s đặt trong configmap; chỉ compose prod bỏ sót.
        var svc = ComposeServiceBlock(ReadRepoFile("docker-compose.prod.yml"), "filestorageservice");

        svc.Should().Contain("ObjectStorage__PublicServiceUrl");
    }

    [Fact]
    public void ProdEnvExample_DoesNotShipGuessableCredentials()
    {
        var env = ReadRepoFile("env.prod.example");

        CodeLines(env).Should().NotContain(l =>
            l.StartsWith("ObjectStorage__AccessKey=minioadmin", StringComparison.Ordinal) ||
            l.StartsWith("ObjectStorage__SecretKey=minioadmin", StringComparison.Ordinal));
    }

    [Fact]
    public void ProdEnvExample_StopsAdvertisingAPublicBaseUrl()
    {
        // Bucket private ⇒ URL "public" chỉ trả 403. Vẫn phát ra thì FE tưởng dùng được, và triệu
        // chứng sẽ hiện ra ở phía người dùng chứ không ở phía cấu hình.
        var env = ReadRepoFile("env.prod.example");

        CodeLines(env).Should().Contain(l => l.Trim() == "ObjectStorage__PublicBaseUrl=");
    }

    // ───────────────────────────────────────────────── Helm

    [Fact]
    public void Helm_DoesNotMakeBucketAnonymouslyReadable()
    {
        var minio = ReadRepoFile("deploy", "helm", "solar-battery", "templates", "infra", "minio.yaml");

        CodeLines(minio).Should().NotContain(l => l.Contains("anonymous set download"));
        minio.Should().Contain("anonymous set none");
    }

    [Fact]
    public void Helm_TakesBothRootCredentialsFromSecret()
    {
        // values.yaml nằm trong Git. Để rootUser ở đó nghĩa là một nửa credential quản trị đã công
        // khai, và nửa còn lại là thứ duy nhất chắn giữa Internet và toàn bộ bucket.
        var minio = ReadRepoFile("deploy", "helm", "solar-battery", "templates", "infra", "minio.yaml");
        var values = ReadRepoFile("deploy", "helm", "solar-battery", "values.yaml");

        CodeLines(minio).Should().NotContain(l => l.Contains(".Values.minio.rootUser"));
        minio.Should().Contain("key: ObjectStorage__AccessKey");
        CodeLines(values).Should().NotContain(l => l.Contains("rootUser:"));
    }

    [Fact]
    public void Helm_GatesConsoleIngressSeparately_AndOffByDefault()
    {
        // Trước đây console dùng chung cờ với S3 API, nên bật đường tải file cho client là đồng thời
        // mở luôn giao diện quản trị ra Internet.
        var minio = ReadRepoFile("deploy", "helm", "solar-battery", "templates", "infra", "minio.yaml");
        var values = ReadRepoFile("deploy", "helm", "solar-battery", "values.yaml");
        var staging = ReadRepoFile("deploy", "helm", "solar-battery", "values-staging.yaml");

        minio.Should().Contain(".Values.minio.consoleIngress.enabled");
        values.Should().MatchRegex(@"consoleIngress:\s*\r?\n\s*enabled:\s*false");
        staging.Should().MatchRegex(@"consoleIngress:\s*\r?\n\s*enabled:\s*false");
    }

    [Fact]
    public void Helm_StopsAdvertisingAPublicBaseUrl()
    {
        var configmap = ReadRepoFile("deploy", "helm", "solar-battery", "templates", "shared", "configmap.yaml");

        CodeLines(configmap).Should().Contain(l =>
            l.TrimStart().StartsWith("ObjectStorage__PublicBaseUrl:", StringComparison.Ordinal)
            && l.TrimEnd().EndsWith("\"\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Helm_KeepsS3ApiIngress_SoPresignedUrlsStillResolve()
    {
        var minio = ReadRepoFile("deploy", "helm", "solar-battery", "templates", "infra", "minio.yaml");

        minio.Should().Contain(".Values.minio.ingress.enabled");
        minio.Should().Contain("files.{{ .Values.global.domain }}");
    }
}
