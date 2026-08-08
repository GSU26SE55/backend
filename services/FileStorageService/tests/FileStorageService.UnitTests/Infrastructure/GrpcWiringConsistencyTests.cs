using System.Text.RegularExpressions;
using FileStorageService.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FileStorageService.UnitTests.Infrastructure;

/// <summary>
/// GH-790 — kênh gRPC nội bộ phải được nối dây <b>khớp nhau</b> ở mọi môi trường.
/// </summary>
/// <remarks>
/// <para>
/// Chỉ kiểm "có khai biến" là chưa đủ, và đó đúng là điểm yếu của bản kiểm đầu tiên tôi viết:
/// khai cổng máy chủ <c>8081</c> nhưng địa chỉ client trỏ <c>:8082</c> thì cả hai phép kiểm có-mặt
/// đều xanh, trong khi không có gì chạy được. Ở đây so hai giá trị với nhau.
/// </para>
/// <para>
/// Hỏng kiểu này không test mã nào bắt được: mọi thứ biên dịch, mọi test xanh, và triệu chứng chỉ
/// hiện ở môi trường thật dưới dạng "service không lên" (thiếu biến) hoặc "file không quét được"
/// (địa chỉ lệch cổng).
/// </para>
/// </remarks>
public class GrpcWiringConsistencyTests
{
    private const string ServerPortKey = "FILE_STORAGE_SERVICE_GRPC_SERVER_PORT";
    private const string ClientAddressKey = "FILE_STORAGE_GRPC_CLIENT_ADDRESS";

    /// <summary>Tên service của FileStorage trong mạng nội bộ (compose lẫn k8s dùng chung tên này).</summary>
    private const string ServiceHost = "filestorageservice";

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

    private static string Read(params string[] segments)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray());
        File.Exists(path).Should().BeTrue($"thiếu file cấu hình {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Đọc file nếu có, trả <c>null</c> nếu không — dùng cho file cục bộ theo từng máy.</summary>
    private static string? TryRead(params string[] segments)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// Nguồn cấu hình <b>có trong Git</b> — bắt buộc phải tồn tại và phải khớp nhau.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chỉ liệt kê file được Git theo dõi. <c>.env</c> và <c>.env.Docker</c> KHÔNG nằm ở đây dù
    /// chúng cũng khai hai biến này: cả hai bị <c>.gitignore</c> và do từng người tự tạo, nên
    /// máy vừa clone hoặc runner CI không hề có chúng.
    /// </para>
    /// <para>
    /// Bản đầu tiên của phép kiểm này gộp chung cả năm nguồn. Đo được: đổi tên <c>.env</c> đi rồi
    /// chạy lại thì <b>4 test đỏ</b> với thông báo "thiếu file cấu hình …/.env" — tức bộ test chỉ
    /// xanh nhờ file riêng của máy tôi, và sẽ đỏ ở mọi nơi khác. Xem
    /// <see cref="LocalEnvFiles_WhenPresent_AgreeWithTheTrackedOnes"/> cho phần kiểm file cục bộ.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string[]> WiringSources()
    {
        var data = new TheoryData<string, string[]>();
        data.Add("dev template (.env.Docker.example)", [".env.Docker.example"]);
        data.Add("production (env.prod.example)", ["env.prod.example"]);
        data.Add("k8s (helm configmap)",
            ["deploy", "helm", "solar-battery", "templates", "shared", "configmap.yaml"]);
        return data;
    }

    /// <summary>File cấu hình CỤC BỘ — có thì kiểm, không có thì thôi.</summary>
    private static readonly (string Label, string[] Path)[] LocalWiringSources =
    [
        ("dev (.env)", [".env"]),
        ("dev docker (.env.Docker)", [".env.Docker"]),
    ];

    /// <summary>Lấy giá trị của một khoá, chấp nhận cả dạng <c>KEY=value</c> lẫn <c>KEY: "value"</c>.</summary>
    private static string? ValueOf(string content, string key)
    {
        var match = Regex.Match(content,
            $@"^\s*{Regex.Escape(key)}\s*[:=]\s*""?([^""\r\n]+?)""?\s*$",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [Theory]
    [MemberData(nameof(WiringSources))]
    public void EverySource_DeclaresBothVariables(string label, string[] path)
    {
        var content = Read(path);

        ValueOf(content, ServerPortKey).Should().NotBeNull(
            $"{label}: thiếu {ServerPortKey} thì FileStorageService ném lỗi ngay lúc khởi động");
        ValueOf(content, ClientAddressKey).Should().NotBeNull(
            $"{label}: thiếu {ClientAddressKey} thì TicketService không biết gọi đi đâu");
    }

    [Theory]
    [MemberData(nameof(WiringSources))]
    public void EverySource_PointsTheClientAtTheSamePortTheServerListensOn(string label, string[] path)
    {
        // ĐÂY là phép kiểm mà bản đầu tiên còn thiếu. Khai 8081 ở máy chủ nhưng client trỏ 8082 thì
        // hai phép kiểm có-mặt vẫn xanh, còn hệ thống thì không có gì chạy.
        var content = Read(path);
        var serverPort = int.Parse(ValueOf(content, ServerPortKey)!);
        var address = ValueOf(content, ClientAddressKey)!;

        Uri.TryCreate(address, UriKind.Absolute, out var uri).Should().BeTrue(
            $"{label}: TicketService yêu cầu URI tuyệt đối, nếu không sẽ ném lỗi lúc dựng DI");

        uri!.Port.Should().Be(serverPort,
            $"{label}: địa chỉ client phải trỏ đúng cổng mà máy chủ lắng nghe");
        uri.Host.Should().Be(ServiceHost,
            $"{label}: phải gọi theo tên service trong mạng nội bộ");
    }

    [Theory]
    [MemberData(nameof(WiringSources))]
    public void EverySource_UsesAPortTheServiceWillAccept(string label, string[] path)
    {
        // Chạy chính luật của Program.cs lên giá trị trong file cấu hình: cách duy nhất để biết
        // service sẽ khởi động được, thay vì chỉ biết "biến có tồn tại".
        var content = Read(path);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GrpcServerPort.PrimaryKey] = ValueOf(content, ServerPortKey),
            })
            .Build();

        var act = () => GrpcServerPort.Resolve(config);

        act.Should().NotThrow($"{label}: giá trị trong file phải qua được chính phép kiểm lúc khởi động");
    }

    [Fact]
    public void AllEnvironments_AgreeOnTheSamePort()
    {
        // Lệch cổng giữa các môi trường không sai về kỹ thuật, nhưng khiến sự cố ở một môi trường
        // không tái hiện được ở môi trường khác — loại rắc rối tốn nhiều thời gian nhất.
        var ports = WiringSources()
            .Select(row => (string[])row[1])
            .Select(path => ValueOf(Read(path), ServerPortKey))
            .Distinct()
            .ToList();

        ports.Should().ContainSingle("mọi môi trường nên dùng chung một cổng gRPC");
    }

    /// <summary>
    /// File cục bộ (<c>.env</c>, <c>.env.Docker</c>) nếu CÓ thì phải khớp các nguồn trong Git.
    /// </summary>
    /// <remarks>
    /// Chúng bị <c>.gitignore</c> nên máy vừa clone và runner CI không có — vì vậy chỉ kiểm khi
    /// tồn tại. Vẫn đáng kiểm: đây chính là file mà người phát triển chạy hằng ngày, lệch cổng ở
    /// đây thì "máy tôi chạy được" mà không ai giải thích nổi tại sao.
    /// </remarks>
    [Fact]
    public void LocalEnvFiles_WhenPresent_AgreeWithTheTrackedOnes()
    {
        var expectedPort = ValueOf(Read(".env.Docker.example"), ServerPortKey);
        expectedPort.Should().NotBeNull("bản mẫu trong Git phải khai cổng gRPC");

        foreach (var (label, path) in LocalWiringSources)
        {
            var content = TryRead(path);
            if (content is null)
                continue;   // máy này không có file đó — không phải lỗi

            var port = ValueOf(content, ServerPortKey);
            var address = ValueOf(content, ClientAddressKey);

            port.Should().NotBeNull($"{label}: có file thì phải khai {ServerPortKey}");
            address.Should().NotBeNull($"{label}: có file thì phải khai {ClientAddressKey}");
            port.Should().Be(expectedPort, $"{label}: phải dùng chung cổng với bản mẫu trong Git");

            Uri.TryCreate(address, UriKind.Absolute, out var uri).Should().BeTrue(
                $"{label}: TicketService yêu cầu URI tuyệt đối");
            uri!.Port.Should().Be(int.Parse(port!), $"{label}: client phải trỏ đúng cổng máy chủ nghe");
            uri.Host.Should().Be(ServiceHost, $"{label}: phải gọi theo tên service trong mạng nội bộ");
        }
    }

    [Fact]
    public void HelmService_ExposesExactlyThePortTheConfigMapAdvertises()
    {
        // Khai địa chỉ mà không mở cổng thì tên miền nội bộ không định tuyến tới đâu cả, và lỗi hiện
        // ra dưới dạng "không quét được file" chứ không phải lỗi cấu hình.
        var configmap = Read("deploy", "helm", "solar-battery", "templates", "shared", "configmap.yaml");
        var port = ValueOf(configmap, ServerPortKey)!;

        var svc = Read("deploy", "helm", "solar-battery", "templates", "services", "filestorageservice.yaml");

        svc.Should().Contain($"containerPort: {port}", "container phải mở đúng cổng đó");
        svc.Should().MatchRegex($@"name: grpc, port: {port}", "Service phải định tuyến đúng cổng đó");
    }

    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.prod.yml")]
    public void EveryCompose_WiresBothSides_AndFailsFastWhenTheyAreMissing(string composeFile)
    {
        // Compose khai tường minh hai biến này cho từng service; đổi một bên mà quên bên kia là đứt
        // kênh mà không có gì báo.
        //
        // `${VAR:?...}` (chứ không phải `${VAR:-mặc định}`) để `docker compose up` DỪNG NGAY khi
        // thiếu. Bản prod trước đây phó mặc cho env_file: thiếu biến thì container vẫn lên rồi chết
        // với InvalidOperationException, và triệu chứng là crash-loop chứ không phải một lỗi cấu hình
        // đọc được.
        var compose = Read(composeFile);

        compose.Should().Contain($"${{{ServerPortKey}:?",
            $"{composeFile}: filestorageservice phải nhận cổng gRPC và dừng ngay nếu thiếu");
        compose.Should().Contain($"${{{ClientAddressKey}:?",
            $"{composeFile}: ticketservice phải nhận địa chỉ gRPC và dừng ngay nếu thiếu");
    }

    /// <summary>Cặp (file compose, bản mẫu env) — thêm môi trường mới thì thêm một dòng.</summary>
    public static TheoryData<string, string> ComposeTemplatePairs()
    {
        var data = new TheoryData<string, string>();
        data.Add("docker-compose.yml", ".env.Docker.example");
        data.Add("docker-compose.prod.yml", "env.prod.example");
        return data;
    }

    [Theory]
    [MemberData(nameof(ComposeTemplatePairs))]
    public void EveryTemplate_DeclaresEveryVariableItsComposeHardRequires(string composeFile, string templateFile)
    {
        // Bản mẫu ghi ở đầu file là "copy rồi chạy". Nhưng compose khai một số biến dạng ${VAR:?...}
        // — thiếu là `docker compose up` dừng ngay. Đo được: làm đúng hướng dẫn đó thì
        // `docker compose config` trả exit 1 ở CẢ HAI cặp (dev vướng biến gRPC, prod vướng CORS).
        //
        // Kiểm theo DANH SÁCH SINH TỪ CHÍNH compose, không phải danh sách chép tay: thêm một biến
        // bắt buộc mới mà quên cập nhật bản mẫu thì test này đỏ ngay.
        var compose = Read(composeFile);
        var template = Read(templateFile);

        // Bỏ dòng chú thích trước khi dò: phần giải thích trong compose có nhắc chuỗi mẫu
        // "${VAR:?...}", quét cả file sẽ bắt nhầm chính lời giải thích đó thành một biến tên "VAR".
        var composeCode = string.Join('\n',
            compose.Split('\n').Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal)));

        var required = Regex.Matches(composeCode, @"\$\{([A-Za-z_][A-Za-z0-9_]*):\?")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        required.Should().NotBeEmpty(
            $"{composeFile} phải có ít nhất một biến bắt buộc để phép kiểm này có nghĩa");

        var declared = Regex.Matches(template, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        required.Where(v => !declared.Contains(v)).Should().BeEmpty(
            $"{templateFile} phải khai đủ mọi biến mà {composeFile} bắt buộc, "
          + "nếu không người dùng bản mẫu sẽ không chạy được");
    }
}
