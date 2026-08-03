using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedInfrastructure.DependencyInjection.Extensions;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

/// <summary>
/// #AUTH-05 (P0) — CORS whitelist. Bộ test này được viết lại 2026-08-01: bản cũ khẳng định
/// "mọi origin đều được phép" — tức là nó **đang bảo vệ chính lỗ hổng cần sửa**.
/// </summary>
public class CorsExtensionsTests
{
    private static CorsPolicy? BuildPolicy(string[]? origins, string environmentName)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(BuildConfig(origins))
            .Build();

        services.AddCorsExtentions(config, new StubEnvironment(environmentName));

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy(AddCORS.PolicyName);
    }

    private static IEnumerable<KeyValuePair<string, string?>> BuildConfig(string[]? origins)
    {
        if (origins is null)
            yield break;
        for (var i = 0; i < origins.Length; i++)
            yield return new KeyValuePair<string, string?>($"{AddCORS.ConfigKey}:{i}", origins[i]);
    }

    [Fact]
    public void WithWhitelist_AllowsOnlyListedOrigins()
    {
        var policy = BuildPolicy(new[] { "https://app.solarbattery.site", "https://admin.solarbattery.site" },
            Environments.Production);

        policy.Should().NotBeNull();
        policy!.IsOriginAllowed("https://app.solarbattery.site").Should().BeTrue();
        policy.IsOriginAllowed("https://admin.solarbattery.site").Should().BeTrue();

        policy.IsOriginAllowed("https://evil.example.com").Should().BeFalse(
            "site lạ gọi API kèm cookie của user đang đăng nhập chính là lỗ hổng #AUTH-05");
        policy.IsOriginAllowed("http://app.solarbattery.site").Should().BeFalse(
            "khác scheme là khác origin — http:// không được ăn theo https://");

        policy.AllowAnyMethod.Should().BeTrue();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void TrailingSlashInConfig_IsNormalized_SoWhitelistStillMatches()
    {
        // `WithOrigins` so khớp chuỗi nguyên văn. Người cấu hình rất dễ dán kèm dấu '/' cuối và rồi
        // whitelist trượt im lặng — chuẩn hoá để tránh mất cả buổi debug.
        var policy = BuildPolicy(new[] { "https://app.solarbattery.site/" }, Environments.Production);

        policy!.IsOriginAllowed("https://app.solarbattery.site").Should().BeTrue();
    }

    [Fact]
    public void EmptyWhitelist_InProduction_ThrowsAtStartup()
    {
        var act = () => BuildPolicy(Array.Empty<string>(), Environments.Production);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AUTH-05*")
            .WithMessage($"*{AddCORS.ConfigKey}*");
        // Cố ý ném thay vì chỉ log: service không lên còn hơn lên với CORS mở toang.
    }

    [Fact]
    public void EmptyWhitelist_InDevelopment_FallsBackToPermissive()
    {
        var policy = BuildPolicy(Array.Empty<string>(), Environments.Development);

        policy.Should().NotBeNull();
        policy!.IsOriginAllowed("http://localhost:5173").Should().BeTrue(
            "dev không nên bị vướng CORS khi chạy FE ở cổng bất kỳ");
    }

    [Fact]
    public void NoConfigurationAtAll_DoesNotThrow_OutsideProduction()
    {
        // Bảo vệ đường gọi cũ (không truyền configuration) — vd test hoặc host tối giản.
        var services = new ServiceCollection();
        var act = () => services.AddCorsExtentions();

        act.Should().NotThrow();
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
