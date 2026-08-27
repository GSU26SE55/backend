using AuthService.Application.Authorization;
using AuthService.Application.Common.Options;
using AuthService.Application.Configuration;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Infrastructure.BackgroundJobs;
using AuthService.Infrastructure.Implements.Helpers;
using AuthService.Infrastructure.Implements.Repositories;
using AuthService.Infrastructure.Implements.Services;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;

namespace AuthService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddAuthServiceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScopedInterface(configuration);
        services.AddSharedInfrastructure(configuration, "AuthService.Application", "Auth Service API");

        // #AUTH-15: register Application-layer consumers (PermissionsChangedConsumer).
        // GH-728 — thêm assembly Infrastructure để MassTransit thấy AuthAuditReplayRequestedConsumer.
        // Thiếu dòng này thì yêu cầu replay bay qua service mà không ai xử lý, và job treo mãi.
        services.AddMessageBus(
            configuration,
            typeof(AuthService.Application.Authorization.PermissionResolver).Assembly,
            typeof(ManageDependencyInjection).Assembly);

        // Consumer của AuthService dùng ProcessOnceAsync để chống xử lý trùng, mà hàm đó cần
        // IInboxStore. Trước đây AuthService không có consumer nào dùng nên chưa đăng ký; thiếu
        // dòng này thì consumer mới chết ngay lúc container dựng, không phải lúc chạy.
        //
        // IConnectionMultiplexer đã được AddIdempotencyKey đăng ký ở Program.cs; cả hai extension
        // dùng TryAdd nên AuthService chỉ giữ một kết nối Redis dùng chung.
        services.AddInboxIdempotency(configuration);

        // Outbox Pattern: override IMessageProducerService bằng OutboxMessagePublisher.
        // Handler publish event → INSERT vào DbContext.OutboxMessages, atomic với business data.
        // OutboxRelayBackgroundService poll bảng outbox và publish thật lên RabbitMQ.
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IMessageProducerService, OutboxMessagePublisher>();
        services.AddHostedService<OutboxRelayBackgroundService>();
        services.AddHostedService<AuditOutboxRelayBackgroundService>();   // Sprint audit #AUDIT-08

        // #AUTH-31: dọn PendingEmail "treo" 24h sau khi OTP expire.
        services.AddHostedService<PendingEmailCleanupBackgroundService>();

        // #AUTH-18 + #AUTH-43: auto-unlock account đã hết lockout sau mỗi 5 phút.
        services.AddHostedService<LockoutReconcileBackgroundService>();

        // #AUTH-49: dọn OTP scratch fields cũ hơn 24h (không bao gồm OtpPurpose=EmailChange).
        services.AddHostedService<ExpiredOtpCleanupBackgroundService>();

        // #AUTH-42: hard-delete account đã soft-delete > 90 ngày.
        services.AddHostedService<AccountHardDeleteBackgroundService>();

        // Reconciliation loop: realtime account events are primary; periodic authoritative
        // snapshots repair missed messages and downstream rows that drifted from AuthService.
        services.AddOptions<AccountProjectionReconciliationOptions>()
            .Bind(configuration.GetSection(AccountProjectionReconciliationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHostedService<AccountProjectionReconciliationBackgroundService>();

        // Concurrent session limit policy. Bind từ section "Session" hoặc dùng default (5).
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));

        // #AUTH-12: device binding cho refresh token (IP/UA cross-check).
        services.Configure<AuthSecurityOptions>(configuration.GetSection(AuthSecurityOptions.SectionName));

        // GH-776 — khoá truy cập cho endpoint OAuth introspection. Bỏ trống ⇒ endpoint từ chối tất
        // cả (fail closed) — xem IntrospectionOptions.
        services.Configure<IntrospectionOptions>(configuration.GetSection(IntrospectionOptions.SectionName));

        // #AUTH-32 + #AUTH-66: JwtSettings options với ValidateDataAnnotations + ValidateOnStart.
        // Deploy thiếu SecretKey/Issuer/Audience → fail fast lúc Build() thay vì runtime crash.
        services.AddOptions<JwtSettingsOptions>()
            .Bind(configuration.GetSection(JwtSettingsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // #AUTH-53: password policy configurable. Bind từ section "PasswordPolicy" + bootstrap static.
        var passwordPolicyOptions = configuration.GetSection(PasswordPolicyOptions.SectionName)
            .Get<PasswordPolicyOptions>() ?? new PasswordPolicyOptions();
        services.Configure<PasswordPolicyOptions>(configuration.GetSection(PasswordPolicyOptions.SectionName));
        AuthService.Application.Validation.PasswordPolicy.Configure(passwordPolicyOptions);

        // Granular permission authorization:
        // [HasPermission("battery.view")] -> policy "perm:battery.view"
        // -> PermissionPolicyProvider tạo policy on-the-fly
        // -> PermissionAuthorizationHandler verify claim "perm".
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuthDb")
                               ?? configuration["AuthDb"]
                               ?? configuration["Auth_Db"]
                               ?? configuration["Auth_DB"]
                               ?? configuration["AUTH_DB"]
                               ?? configuration["AUTH_Db"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing connection string. Expected ConnectionStrings__AuthDb, AuthDb, Auth_Db, Auth_DB, AUTH_DB, or AUTH_Db.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetService<ApplicationDbContext>()!);
    }

    private static void AddScopedInterface(this IServiceCollection service, IConfiguration configuration)
    {
        service.AddScoped<IAuthUnitOfWork, UnitOfWork>();
        // GH-794 — giành quyền publish từng dòng outbox, chặn hai replica cùng gửi một message.
        service.AddScoped<IOutboxClaimService, Implements.Services.OutboxClaimService>();
        service.AddScoped<IJwtHelper, JwtHelper>();
        // #AUTH-21: configure timeout 10s cho HttpClient. Retry sẽ thực hiện trong helper bằng
        // try/catch + delay (tránh thêm Polly dependency cho chỉ 1 endpoint).
        service.AddHttpClient<IGoogleOAuthHelper, GoogleOAuthHelper>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        // #AUTH-70 + #AUTH-83: Scoped thay Singleton — tránh cache state nếu future work-factor đọc từ DB
        // hoặc cấu hình per-request. Hiện tại PasswordHasher stateless nhưng Scoped future-proof.
        service.AddScoped<IPasswordHasher, PasswordHasher>();
        service.AddScoped<AuthDataSeeder>();
        service.AddHttpContextAccessor();

        // #AUTH-38: abstraction cho DateTime.UtcNow — mockable trong test.
        service.AddSingleton<ISystemClock, SystemClock>();

        // #AUTH-54: Token Revocation List (TRL) — Redis-backed jti blacklist.
        service.AddSingleton<ITokenRevocationStore, RedisTokenRevocationStore>();

        // #AUTH-58: 2FA SMS OTP store.
        service.AddSingleton<ITwoFactorSmsOtpStore, RedisTwoFactorSmsOtpStore>();

        // 2FA Hardening (GH-295)
        service.AddSingleton<ITotpService, TotpService>();
        service.AddSingleton<ITwoFactorSecretProtector, TwoFactorSecretProtector>();
        service.AddSingleton<ITwoFactorChallengeStore, TwoFactorChallengeStore>();
        service.AddSingleton<ITwoFactorPendingStore, TwoFactorPendingStore>();
        // #AUTH-51: cross-device 2FA confirm — Redis-backed token store, TTL 10 phút.
        service.AddSingleton<ITwoFactorCrossDeviceConfirmStore, TwoFactorCrossDeviceConfirmStore>();
        service.AddSingleton<IBackupCodeGenerator, BackupCodeGenerator>();
        service.AddScoped<IAuthTokenIssuer, AuthTokenIssuer>();
    }
}
