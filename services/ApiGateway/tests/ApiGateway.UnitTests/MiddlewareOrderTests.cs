using FluentAssertions;
using Xunit;

namespace ApiGateway.UnitTests;

/// <summary>
/// GH-800 — thứ tự middleware quyết định dashboard có hết 5xx giả hay không.
/// </summary>
/// <remarks>
/// <para>
/// Bộ đếm <c>http_requests_received_total</c> của <c>UseHttpMetrics()</c> đọc
/// <c>Response.StatusCode</c> SAU khi phần còn lại của pipeline chạy xong. Muốn nó thấy trạng thái
/// đã chuẩn hoá thì <c>ClientDisconnectStatusMiddleware</c> phải nằm BÊN TRONG nó — tức được đăng ký
/// NGAY SAU trong <c>Program.cs</c>.
/// </para>
/// <para>
/// Đảo hai dòng này thì mọi test khác vẫn xanh, chỉ có dashboard là lại đếm 5xx giả — đúng loại hỏng
/// không ai phát hiện cho tới lúc nhìn biểu đồ. Vì vậy nó cần một khẳng định riêng.
/// </para>
/// </remarks>
public class MiddlewareOrderTests
{
    private static string ProgramSource
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Không tìm thấy gốc repo từ " + AppContext.BaseDirectory);

            var path = Path.Combine(dir!.FullName, "services", "ApiGateway", "src", "ApiGateway", "Program.cs");
            File.Exists(path).Should().BeTrue($"thiếu {path}");
            return File.ReadAllText(path);
        }
    }

    private static IReadOnlyList<string> CodeLines(string text)
        => text.Split('\n')
               .Select(l => l.TrimEnd('\r'))
               .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
               .ToList();

    [Fact]
    public void ClientDisconnectMiddleware_IsRegisteredInsideHttpMetrics()
    {
        var lines = CodeLines(ProgramSource);

        var metricsAt = lines.ToList().FindIndex(l => l.Contains("UseHttpMetrics()"));
        var disconnectAt = lines.ToList().FindIndex(l => l.Contains("ClientDisconnectStatusMiddleware"));

        metricsAt.Should().BeGreaterThan(-1, "gateway phải bật Prometheus HTTP metrics");
        disconnectAt.Should().BeGreaterThan(-1, "gateway phải chuẩn hoá trạng thái khi client ngắt");
        disconnectAt.Should().BeGreaterThan(metricsAt,
            "đăng ký sau UseHttpMetrics = nằm bên trong nó = bộ đếm nhìn thấy trạng thái đã chuẩn hoá");
    }

    [Fact]
    public void RequestLogging_StaysOutside_SoItSeesTheFinalStatus()
    {
        var lines = CodeLines(ProgramSource);

        var loggingAt = lines.ToList().FindIndex(l => l.Contains("RequestLoggingMiddleware"));
        var disconnectAt = lines.ToList().FindIndex(l => l.Contains("ClientDisconnectStatusMiddleware"));

        loggingAt.Should().BeGreaterThan(-1);
        loggingAt.Should().BeLessThan(disconnectAt,
            "log nằm ngoài nên đọc được trạng thái sau khi đã chuẩn hoá");
    }
}
