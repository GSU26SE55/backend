using System.Text.RegularExpressions;
using EmailService.Infrastructure.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EmailService.UnitTests.Templates;

/// <summary>
/// Kiểm tra các file template thật trong <c>EmailService.Api/wwwroot/email-templates</c>
/// (không mock renderer), bằng chính <see cref="EmailTemplateRenderer"/> mà production dùng.
///
/// Bắt 2 loại lỗi thực tế đã gặp:
/// 1. Template tham chiếu key mà consumer KHÔNG truyền → <c>EmailTemplateRenderer</c> để nguyên
///    chuỗi <c>{{Key}}</c> ⇒ khách hàng nhận email có placeholder thô.
/// 2. Regression layout: <c>min-width:1000px</c> / <c>width:70%</c> / thiếu viewport ⇒ email
///    hiển thị bé tí và viền trắng dày trên mobile.
/// </summary>
public class EmailTemplateFilesTests
{
    private static readonly Regex LeftoverPlaceholder =
        new(@"\{\{\s*(?<key>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    private static readonly string[] TemplateRootSegments =
        ["services", "EmailService", "src", "EmailService.Api", "wwwroot", "email-templates"];

    /// <summary>
    /// Đường dẫn tới wwwroot của project Api, dò ngược từ thư mục bin của test.
    ///
    /// Neo theo file solution chứ KHÔNG dò theo tên thư mục "services": macOS/Windows dùng
    /// filesystem case-insensitive nên thư mục <c>Services/</c> của chính project test cũng khớp
    /// "services" và vòng lặp dừng sớm ở sai chỗ.
    /// </summary>
    private static string TemplateRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("SolarBatteryMaintainance.slnx").Length == 0)
                dir = dir.Parent;

            Assert.True(dir is not null,
                $"Không tìm thấy repo root (SolarBatteryMaintainance.slnx) từ {AppContext.BaseDirectory}");

            var path = Path.Combine(new[] { dir!.FullName }.Concat(TemplateRootSegments).ToArray());
            Assert.True(Directory.Exists(path), $"Không thấy thư mục template tại '{path}'");
            return path;
        }
    }

    private static EmailTemplateRenderer BuildRenderer()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(Path.GetDirectoryName(TemplateRoot)!);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailTemplates:Path"] = TemplateRoot,
                ["EmailTemplates:CacheDisabled"] = "true",
            })
            .Build();

        return new EmailTemplateRenderer(env.Object, config);
    }

    /// <summary>Bộ key mà consumer tương ứng thực sự truyền vào — phải khớp code production.</summary>
    public static TheoryData<string, string[]> TemplateContracts() => new()
    {
        { EmailTemplates.OtpRegister, ["AppName", "UserName", "Otp", "ExpireMinutes"] },
        { EmailTemplates.OtpPasswordReset, ["AppName", "UserName", "Otp", "ExpireMinutes"] },
        { EmailTemplates.OtpEmailChange, ["AppName", "UserName", "Otp", "ExpireMinutes", "PendingEmail"] },
        { EmailTemplates.AdminInvite, ["AppName", "UserName", "Email", "Role", "AcceptUrl", "InvitationToken", "ExpiresAt"] },
        { EmailTemplates.NotificationGeneric, ["AppName", "Subject", "Body"] },
        { EmailTemplates.SuspiciousLogin, ["AppName", "UserName", "IpAddress", "UserAgent", "Reason", "DetectedAt"] },
        { EmailTemplates.RefreshTokenReuse, ["AppName", "UserName", "IpAddress", "UserAgent", "DetectedAt", "RevokedSessions"] },
    };

    public static TheoryData<string> AllTemplateNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Directory.GetFiles(TemplateRoot, "*.html").Select(Path.GetFileNameWithoutExtension))
            data.Add(name!);
        return data;
    }

    /// <summary>
    /// Key consumer có truyền nhưng template CỐ Ý không hiển thị — kèm lý do.
    /// Truyền thừa thì vô hại; ngược lại (template gọi key không ai truyền) mới là lỗi thật.
    /// </summary>
    private static readonly Dictionary<string, string[]> IntentionallyUnusedKeys = new()
    {
        // Token thô không cần hiện cho người dùng — nó đã nằm sẵn trong AcceptUrl.
        [EmailTemplates.AdminInvite] = ["InvitationToken"],
    };

    [Theory]
    [MemberData(nameof(TemplateContracts))]
    public async Task Template_RendersWithConsumerKeys_LeavingNoRawPlaceholder(string templateName, string[] keys)
    {
        var values = keys.ToDictionary(k => k, k => (string?)$"VAL-{k}");

        var html = await BuildRenderer().RenderAsync(templateName, values);

        // Chiều quan trọng: template KHÔNG được tham chiếu key mà consumer không truyền —
        // EmailTemplateRenderer để nguyên "{{Key}}" ⇒ khách hàng thấy chuỗi thô trong mail.
        var leftovers = LeftoverPlaceholder.Matches(html).Select(m => m.Groups["key"].Value).Distinct().ToList();
        leftovers.Should().BeEmpty(
            $"template '{templateName}' tham chiếu key mà consumer không truyền → khách hàng sẽ thấy "
            + $"chuỗi thô trong email: {string.Join(", ", leftovers)}");

        var skip = IntentionallyUnusedKeys.TryGetValue(templateName, out var s) ? s : [];
        foreach (var key in keys.Except(skip))
            html.Should().Contain($"VAL-{key}", $"'{templateName}' nên dùng tới key '{key}'");
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Template_HasResponsiveEmailLayout(string templateName)
    {
        var html = File.ReadAllText(Path.Combine(TemplateRoot, $"{templateName}.html"));

        html.Should().Contain("name=\"viewport\"",
            $"'{templateName}' thiếu meta viewport → client mobile tự zoom-out, chữ bé");
        html.Should().Contain("max-width:600px");
        html.Should().NotContain("min-width:1000px",
            $"'{templateName}' còn min-width:1000px → mobile phải scale nhỏ toàn bộ email");
        html.Should().NotContain("width:70%",
            $"'{templateName}' còn width:70% → chừa gutter trắng dày hai bên");
        html.Should().NotContain("margin:50px auto");
    }

    [Theory]
    [MemberData(nameof(AllTemplateNames))]
    public void Template_IsWellFormedHtmlDocument(string templateName)
    {
        var html = File.ReadAllText(Path.Combine(TemplateRoot, $"{templateName}.html"));

        html.Should().StartWith("<!DOCTYPE html>");
        html.TrimEnd().Should().EndWith("</html>");
        html.Should().Contain("charset=UTF-8");

        CountOccurrences(html, "<table").Should().Be(CountOccurrences(html, "</table>"));
        CountOccurrences(html, "<tr").Should().Be(CountOccurrences(html, "</tr>"));
    }

    [Fact]
    public void EveryTemplateConstant_HasFileOnDisk()
    {
        string[] declared =
        [
            EmailTemplates.OtpRegister, EmailTemplates.OtpPasswordReset, EmailTemplates.OtpEmailChange,
            EmailTemplates.AdminInvite, EmailTemplates.NotificationGeneric,
            EmailTemplates.SuspiciousLogin, EmailTemplates.RefreshTokenReuse,
        ];

        foreach (var name in declared)
        {
            File.Exists(Path.Combine(TemplateRoot, $"{name}.html"))
                .Should().BeTrue($"hằng EmailTemplates.{name} phải có file '{name}.html' trên disk — "
                                 + "thiếu file thì consumer rơi vào fallback hoặc ném FileNotFoundException");
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
