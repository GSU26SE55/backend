using FluentAssertions;
using Xunit;

namespace SharedInfrastructure.UnitTests.RateLimiting;

/// <summary>
/// Kiểm tra cách 9 service ráp limiter vào pipeline, bằng cách đọc trực tiếp <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// Vì sao phải soi mã nguồn thay vì test hành vi: đặt <c>UseStandardRateLimiter()</c> trước
/// <c>UseAuthentication()</c> thì service vẫn chạy, vẫn trả 200, mọi test khác vẫn xanh —
/// chỉ có điều <c>HttpContext.User</c> lúc đó còn rỗng nên MỌI request đều bị xếp vào bậc ẩn danh
/// 60 req/30s. Đúng loại hỏng không ai phát hiện cho tới khi người dùng thật báo bị chặn.
/// Đây là lỗi đã có thật ở AuthService và SmsService trước khi sửa.
/// </remarks>
public class RateLimiterWiringTests
{
    private static readonly string[] ServiceProgramPaths =
    [
        "services/ApiGateway/src/ApiGateway/Program.cs",
        "services/AuthService/src/AuthService.Api/Program.cs",
        "services/BatteryService/src/BatteryService.Api/Program.cs",
        "services/TicketService/src/TicketService.Api/Program.cs",
        "services/NotificationService/src/NotificationService.Api/Program.cs",
        "services/FileStorageService/src/FileStorageService.Api/Program.cs",
        "services/EmailService/src/EmailService.Api/Program.cs",
        "services/SmsService/src/SmsService.Api/Program.cs",
        "services/AuditAggregatorService/src/AuditAggregatorService.Api/Program.cs"
    ];

    public static TheoryData<string> AllServices()
    {
        var data = new TheoryData<string>();
        foreach (var path in ServiceProgramPaths)
            data.Add(path);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllServices))]
    public void EveryService_RegistersStandardRateLimiting(string relativePath)
    {
        ReadProgram(relativePath).Should().Contain("AddStandardRateLimiting(",
            $"{relativePath} phải đăng ký hạn mức nền — không service nào được là ngoại lệ");
    }

    [Theory]
    [MemberData(nameof(AllServices))]
    public void EveryService_EnablesTheLimiterInThePipeline(string relativePath)
    {
        ReadProgram(relativePath).Should().Contain("UseStandardRateLimiter()",
            $"{relativePath} đăng ký limiter mà không bật thì hạn mức không có tác dụng");
    }

    [Theory]
    [MemberData(nameof(AllServices))]
    public void Limiter_RunsAfterAuthentication_SoTokenHoldersGetTheHigherTier(string relativePath)
    {
        var lines = CodeLines(ReadProgram(relativePath));

        var authenticationAt = IndexOf(lines, "UseAuthentication()");
        if (authenticationAt < 0)
            return; // EmailService không có tầng xác thực — mọi request đều là ẩn danh, không có ràng buộc thứ tự.

        var limiterAt = IndexOf(lines, "UseStandardRateLimiter()");
        limiterAt.Should().BeGreaterThan(authenticationAt,
            $"{relativePath}: đặt trước UseAuthentication thì HttpContext.User còn rỗng, "
            + "mọi request kể cả đã đăng nhập đều bị áp bậc ẩn danh 60 req/30s");
    }

    [Theory]
    [MemberData(nameof(AllServices))]
    public void Limiter_RunsAfterAuthorization_SoIotDevicesGetTheHigherTier(string relativePath)
    {
        var lines = CodeLines(ReadProgram(relativePath));

        var authorizationAt = IndexOf(lines, "UseAuthorization()");
        if (authorizationAt < 0)
            return;

        var limiterAt = IndexOf(lines, "UseStandardRateLimiter()");
        limiterAt.Should().BeGreaterThan(authorizationAt,
            $"{relativePath}: thiết bị IoT dùng [Authorize(AuthenticationSchemes = \"ApiKey\")] nên chỉ có "
            + "danh tính sau UseAuthorization; đặt limiter trước đó là đẩy toàn bộ thiết bị xuống bậc ẩn danh");
    }

    [Theory]
    [MemberData(nameof(AllServices))]
    public void NoService_CallsRawUseRateLimiter(string relativePath)
    {
        var lines = CodeLines(ReadProgram(relativePath));

        lines.Should().NotContain(line => line.Contains("app.UseRateLimiter()"),
            $"{relativePath}: dùng UseStandardRateLimiter() để ràng buộc thứ tự được ghi lại một chỗ duy nhất");
    }

    /// <summary>
    /// <c>AddRateLimiter</c> dùng options pattern nên mọi lần gọi ghi vào cùng một object.
    /// Hai nơi cùng đặt <c>OnRejected</c> thì nơi đăng ký sau ghi đè nơi trước — response 429 sẽ đổi
    /// hình dạng theo thứ tự đăng ký, một sai lệch rất khó lần ra.
    /// </summary>
    /// <summary>
    /// Gateway TỰ ĐỌC <c>X-Client-Ip</c> để chọn bộ đếm, nên phải xoá giá trị client gắn vào trước khi
    /// limiter chạy. Thiếu bước này, kẻ tấn công đổi header mỗi request là mở vô số bộ đếm và hạn mức
    /// 60 req/30s ở biên mất tác dụng — mà mọi test hành vi khác vẫn xanh.
    /// </summary>
    [Fact]
    public void Gateway_StripsClientSuppliedClientIpHeader_BeforeTheLimiterRuns()
    {
        var lines = CodeLines(ReadProgram("services/ApiGateway/src/ApiGateway/Program.cs"));

        var sanitizerAt = IndexOf(lines, "UseClientIpHeaderSanitizer()");
        var limiterAt = IndexOf(lines, "UseStandardRateLimiter()");

        sanitizerAt.Should().BeGreaterThan(-1, "gateway phải xoá X-Client-Ip do client tự gắn");
        limiterAt.Should().BeGreaterThan(sanitizerAt,
            "xoá sau khi limiter đã đọc header thì không còn tác dụng gì");
    }

    [Fact]
    public void OnRejected_IsDefinedInExactlyOnePlace()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources())
        {
            if (file.EndsWith("StandardRateLimitingExtensions.cs", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            if (text.Contains("OnRejected", StringComparison.Ordinal) && !IsOnlyInComment(text))
                offenders.Add(Path.GetRelativePath(RepoRoot, file));
        }

        offenders.Should().BeEmpty(
            "chỉ StandardRateLimitingExtensions được đặt OnRejected; các file này cũng đặt: "
            + string.Join(", ", offenders));
    }

    private static bool IsOnlyInComment(string text)
    {
        return text
            .Split('\n')
            .Where(line => line.Contains("OnRejected", StringComparison.Ordinal))
            .All(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                         || line.TrimStart().StartsWith("///", StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateProductionSources()
    {
        foreach (var root in new[] { Path.Combine(RepoRoot, "services"), Path.Combine(RepoRoot, "shared", "src") })
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    private static string ReadProgram(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"thiếu {path}");
        return File.ReadAllText(path);
    }

    private static IReadOnlyList<string> CodeLines(string text)
    {
        return text.Split('\n')
                   .Select(line => line.TrimEnd('\r'))
                   .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                   .ToList();
    }

    private static int IndexOf(IReadOnlyList<string> lines, string needle)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

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
}
