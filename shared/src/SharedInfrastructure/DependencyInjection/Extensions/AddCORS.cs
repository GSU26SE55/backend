using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedInfrastructure.DependencyInjection.Extensions;

/// <summary>
/// #AUTH-05 (P0) — CORS whitelist thay cho <c>AllowAll</c>.
///
/// <para><b>Trước 2026-08-01:</b> policy đặt cứng <c>SetIsOriginAllowed(origin =&gt; true)</c> kèm
/// <c>AllowCredentials()</c> — nghĩa là <b>bất kỳ website nào</b> trên Internet cũng gọi được API
/// bằng cookie/credential của người dùng đang đăng nhập. Đây là lỗ hổng P0.</para>
///
/// <para><b>Nay:</b> đọc danh sách origin từ config <c>Cors:AllowedOrigins</c> (mảng). Có giá trị thì
/// siết đúng danh sách đó. Không có giá trị thì:</para>
/// <list type="bullet">
///   <item><b>Development</b> — vẫn cho qua để dev khỏi vướng, nhưng in cảnh báo rõ ràng.</item>
///   <item><b>Production</b> — <b>NÉM LỖI NGAY LÚC KHỞI ĐỘNG</b>. Cố ý: thà service không lên còn hơn
///   lên với CORS mở toang. Chỉ log cảnh báo thì sẽ không ai đọc, và lỗ hổng P0 tồn tại tiếp.</item>
/// </list>
///
/// <para>⚠️ <b>Trước khi deploy production phải set</b> <c>Cors__AllowedOrigins__0</c>,
/// <c>Cors__AllowedOrigins__1</c>… (xem <c>.env.Docker</c>). Danh sách domain do <b>Leader chốt</b> —
/// đó là phần duy nhất của #AUTH-05 còn treo, không phải phần cơ chế.</para>
/// </summary>
public static class AddCORS
{
    /// <summary>Tên policy dùng chung. Đổi tên là phải sửa cả <c>app.UseCors(...)</c> ở 7 service.</summary>
    public const string PolicyName = "AppCors";

    /// <summary>Khoá config chứa mảng origin được phép.</summary>
    public const string ConfigKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddCorsExtentions(
        this IServiceCollection service,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        var origins = (configuration?.GetSection(ConfigKey).Get<string[]>() ?? Array.Empty<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            // "https://x.com/" và "https://x.com" là cùng origin, nhưng WithOrigins so khớp chuỗi
            // nguyên văn — không cắt dấu '/' cuối là whitelist trượt mà không ai hiểu vì sao.
            .Select(o => o.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isProduction = environment?.IsProduction() ?? false;

        if (origins.Length == 0 && isProduction)
        {
            throw new InvalidOperationException(
                $"[#AUTH-05] Missing configuration '{ConfigKey}' in the Production environment. " +
                "CORS would have to allow EVERY origin — that is a P0 vulnerability, so the service refuses to start. " +
                "Set the environment variables Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, ... " +
                "with the FE/Mobile domain list approved by the Leader.");
        }

        service.AddCors();
        service.AddLogging();
        service.AddOptions<CorsOptions>().Configure<ILoggerFactory>((options, loggerFactory) =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (origins.Length > 0)
                {
                    policy.WithOrigins(origins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
                else
                {
                    // Chỉ tới được nhánh này ở Development (Production đã ném ở trên).
                    // KHÔNG dùng AllowAnyOrigin() vì nó không đi cùng AllowCredentials() được.
                    loggerFactory.CreateLogger(typeof(AddCORS).FullName!).LogWarning(
                        $"[CORS][WARNING] '{ConfigKey}' is empty — allowing EVERY origin. " +
                        "Acceptable only in Development. Production will not start without this key.");
                    policy.SetIsOriginAllowed(_ => true)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            });
        });
        return service;
    }
}
