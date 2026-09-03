using BatteryService.Application.Anomaly;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Infrastructure.BackgroundServices;
using BatteryService.Infrastructure.Consumers;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Implements.Services;
using BatteryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddBatteryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddScoped<IBatteryUnitOfWork, UnitOfWork>();
        services.AddSharedInfrastructure(configuration, "BatteryService.Application", "Battery Service API");
        services.AddMessageBus(configuration, typeof(BatteryAccountActivatedConsumer).Assembly);
        services.AddInboxIdempotency(configuration);

        // Anomaly engine config (Sprint 3) — service/AnomalyRules dùng options này
        services.Configure<AnomalyEngineOptions>(configuration.GetSection(AnomalyEngineOptions.SectionName));
        services.AddOptions<MaintenanceScheduleOptions>()
            .Bind(configuration.GetSection(MaintenanceScheduleOptions.SectionName))
            .Validate(options => options.DefaultCycleMonths > 0,
                "Battery:MaintenanceSchedule:DefaultCycleMonths must be greater than zero.")
            .Validate(options => options.LeadDays >= 0,
                "Battery:MaintenanceSchedule:LeadDays must not be negative.")
            .Validate(options => options.PollIntervalSeconds > 0,
                "Battery:MaintenanceSchedule:PollIntervalSeconds must be greater than zero.")
            .Validate(options => options.BatchSize > 0,
                "Battery:MaintenanceSchedule:BatchSize must be greater than zero.")
            .Validate(options => IsValidTimeZone(options.TimeZoneId),
                "Battery:MaintenanceSchedule:TimeZoneId is invalid.")
            .ValidateOnStart();

        // Background-only services — CQRS không cần thiết vì không expose qua REST.
        // Background worker chỉ làm cron trigger, logic ở service này.
        services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
        services.AddScoped<IAlertEscalationService, AlertEscalationService>();
        services.AddScoped<IAlertAutoResolveService, AlertAutoResolveService>();
        services.AddScoped<IOutboxRelayService, OutboxRelayService>();
        // Singleton: cùng một instance phải được cả handler (scoped) lẫn background service dùng
        // chung, nếu không tín hiệu đánh thức rơi vào một semaphore mà không ai chờ.
        services.AddSingleton<IOutboxSignal, OutboxSignal>();
        services.AddScoped<SharedContracts.Interfaces.IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();

        // Sprint 7 B4 (§31.7) — cascade risk assessment (rule-based).
        services.AddScoped<BatteryService.Application.Services.ICascadeRiskCalculator, BatteryService.Application.Services.CascadeRiskCalculator>();
        services.AddScoped<BatteryService.Application.Services.ICascadeRiskService, BatteryService.Application.Services.CascadeRiskService>();
        services.AddScoped<BatteryService.Application.Services.IMaintenanceScheduleService, BatteryService.Application.Services.MaintenanceScheduleService>();

        // Sprint IoT-1 (#243) — per-device API key.
        services.AddScoped<IIotApiKeyService, IotApiKeyService>();

        // Import dữ liệu bên thứ ba (Admin tải file CSV lên).
        services.Configure<BatteryService.Application.Import.ImportOptions>(
            configuration.GetSection(BatteryService.Application.Import.ImportOptions.SectionName));
        services.AddScoped<BatteryService.Application.Import.IImportFileParser,
            BatteryService.Application.Import.CsvImportFileParser>();
        services.AddScoped<BatteryService.Application.Import.IImportWorkbookSplitter,
            BatteryService.Application.Import.ImportWorkbookSplitter>();
        services.AddScoped<BatteryService.Application.Import.IImportRowValidator,
            BatteryService.Application.Import.ImportRowValidator>();
        services.AddScoped<BatteryService.Application.Import.IBatteryTypeResolver,
            BatteryService.Application.Import.BatteryTypeResolver>();
        services.AddScoped<BatteryService.Application.Import.IImportCommitService,
            BatteryService.Application.Import.ImportCommitService>();
        services.AddHostedService<ImportBatchProcessorBackgroundService>();
        services.AddHostedService<ImportRetentionBackgroundService>();

        // Sprint IoT-2 #IoT2-38 — Prometheus IoT metrics recorder.
        services.AddSingleton<IIotMetricsRecorder, BatteryService.Infrastructure.Observability.IotMetricsRecorder>();

        // Sprint 7 #118 — environmental incident metrics (cho AlertManager rule env-monitoring).
        services.AddSingleton<BatteryService.Application.Interfaces.IEnvironmentalMetricsRecorder,
            BatteryService.Infrastructure.Observability.EnvironmentalMetricsRecorder>();

        // Sprint 7 #117 — battery health aggregate gauge (cho Grafana "Battery Health").
        services.AddSingleton<BatteryService.Application.Interfaces.IBatteryHealthMetricsRecorder,
            BatteryService.Infrastructure.Observability.BatteryHealthMetricsRecorder>();

        // Sprint IoT-2 #IoT2-28 — Cross-source validation (Bms vs IoT mismatch).
        services.AddScoped<BatteryService.Application.Services.ICrossSourceValidationService, BatteryService.Application.Services.CrossSourceValidationService>();
        services.AddHostedService<CrossSourceValidationBackgroundService>();

        // Sprint IoT-2 #IoT2-34 — Redis cache invalidation cho calibration.
        services.AddScoped<BatteryService.Application.Services.IIotCalibrationCache, BatteryService.Infrastructure.Implements.Services.IotCalibrationCache>();

        // Sprint IoT-2 #IoT2-33 — Calibration expiry notification (daily).
        services.AddHostedService<CalibrationExpiryNotificationBackgroundService>();

        // Sprint IoT-2 #IoT2-38 — Devices online gauge refresher.
        services.AddHostedService<DevicesOnlineGaugeBackgroundService>();

        // Sprint IoT-1 (#248) — offline detection.
        services.AddScoped<IIotDeviceOfflineDetectionService, IotDeviceOfflineDetectionService>();
        services.AddScoped<IIotDeviceAvailabilityService, IotDeviceAvailabilityService>();
        services.AddHostedService<IotDeviceOfflineDetectionBackgroundService>();

        // Sprint IoT-1 (#253) — MQTT bridge (P3, optional).
        services.Configure<BatteryService.Infrastructure.Mqtt.MqttOptions>(configuration.GetSection(BatteryService.Infrastructure.Mqtt.MqttOptions.SectionName));

        // GH-784 — cấp điểm kết nối broker cho luồng tạo/xoay khoá thiết bị. Trước đây DTO có sẵn
        // MqttBrokerHost/Port nhưng không nơi nào gán ⇒ luôn null.
        services.AddScoped<Application.Interfaces.IMqttBrokerEndpointProvider,
            BatteryService.Infrastructure.Mqtt.MqttBrokerEndpointProvider>();

        // GH-784 — đưa thông tin đăng nhập thiết bị xuống file passwd của broker. Không có nó thì
        // API cấp credential xong nhưng Mosquitto không hề biết, và thiết bị nhận "not authorised".
        // IOT3-29 — MỘT instance dùng cho cả hai vai: worker nền (vòng quét 60s) và
        // IMqttPasswordFileSync (đồng bộ tức thì sau khi cấp/xoay khoá).
        //
        // Đăng ký bằng AddHostedService<T>() THUẦN sẽ tạo instance riêng mà container không
        // resolve lại được, nên handler không có cách nào gọi SyncOnceAsync. Khuôn ba dòng dưới
        // giống hệt IMqttBridgePublisher ngay bên dưới.
        services.AddSingleton<BatteryService.Infrastructure.Mqtt.MqttPasswordFileSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<BatteryService.Infrastructure.Mqtt.MqttPasswordFileSyncService>());
        services.AddSingleton<Application.Interfaces.IMqttPasswordFileSync>(
            sp => sp.GetRequiredService<BatteryService.Infrastructure.Mqtt.MqttPasswordFileSyncService>());
        services.AddSingleton<BatteryService.Infrastructure.Mqtt.MqttBridgeBackgroundService>();
        services.AddSingleton<BatteryService.Application.Services.IMqttBridgePublisher>(sp => sp.GetRequiredService<BatteryService.Infrastructure.Mqtt.MqttBridgeBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatteryService.Infrastructure.Mqtt.MqttBridgeBackgroundService>());

        services.AddHostedService<ThresholdCheckBackgroundService>();
        services.AddHostedService<AlertEscalationBackgroundService>();
        services.AddHostedService<AlertAutoResolveBackgroundService>();
        services.AddHostedService<BatteryService.Infrastructure.BackgroundJobs.BatteryAuditOutboxRelayBackgroundService>(); // Sprint audit #AUDIT-21
        services.AddHostedService<OutboxRelayBackgroundService>();

        // Sprint 7 B4 (§31.7) — recompute cascade risk mỗi 5 phút.
        services.AddHostedService<CascadeRiskBackgroundService>();
        services.AddHostedService<MaintenanceScheduleBackgroundService>();

        // Sprint 7 #117 — refresh battery health gauge mỗi 60s.
        services.AddHostedService<BatteryHealthGaugeBackgroundService>();

        // Sprint 5B #90/#92 — Open-Meteo client + WeatherSync.
        services.Configure<WeatherSyncOptions>(configuration.GetSection(WeatherSyncOptions.SectionName));
        services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WeatherSyncOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opt.HttpTimeoutSeconds));
            })
            .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
                .OrResult(r => (int)r.StatusCode == 429)
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))));
        services.AddHostedService<WeatherSyncBackgroundService>();

        // BE-AI — AI bridge (gRPC primary → HTTP fallback) + SohPredictionBackgroundService.
        // GH-780 — chặn cấu hình bất khả thi NGAY LÚC KHỞI ĐỘNG. Trước đây `Ai:MinReadings` đóng
        // cả hai vai (ngưỡng lịch sử + số dòng payload), nên đặt 29 hay 31 là service vẫn lên bình
        // thường rồi mọi prediction bị AI từ chối im lặng. Sai cấu hình thì phải gãy ở chỗ dễ thấy
        // nhất — lúc bật service — chứ không phải hiện ra dưới dạng "AI không chạy nữa".
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(o => o.MinReadings >= AiOptions.WindowSize,
                $"Ai:MinReadings must be >= {AiOptions.WindowSize} — the AI rejects any payload that "
                + $"does not have exactly {AiOptions.WindowSize} rows, so requiring fewer samples still makes the payload impossible to build.")
            .Validate(o => o.MaxScanReadings >= o.MinReadings,
                "Ai:MaxScanReadings must be >= Ai:MinReadings — scanning back fewer rows than the threshold never yields enough samples.")
            .Validate(o => o.IntervalMinutes > 0, "Ai:IntervalMinutes must be greater than 0.")
            .Validate(o => o.TimeoutSeconds > 0, "Ai:TimeoutSeconds must be greater than 0.")
            .ValidateOnStart();
        var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        // gRPC channel (primary) — 1 AiServiceClient dùng chung cho Predict + Prescribe wrapper.
        services.AddGrpcClient<AiModule.V1.AiService.AiServiceClient>(o =>
        {
            o.Address = new Uri(aiOptions.GrpcAddress);
        });
        services.AddScoped<Implements.Ai.AiPredictionGrpcClient>();
        services.AddScoped<Implements.Ai.AiPrescriptionGrpcClient>();
        services.AddScoped<Implements.Ai.AiHealthGrpcClient>();

        // HTTP clients (fallback) — Polly retry giống OpenMeteo. BaseUrl = FastAPI :8000.
        var aiRetry = HttpPolicyExtensions.HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
        services.AddHttpClient<Implements.Ai.AiPredictionHttpClient>((sp, http) =>
            {
                http.BaseAddress = new Uri(aiOptions.HttpBaseUrl);
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, aiOptions.TimeoutSeconds));
            })
            .AddPolicyHandler(aiRetry);
        services.AddHttpClient<Implements.Ai.AiPrescriptionHttpClient>((sp, http) =>
            {
                http.BaseAddress = new Uri(aiOptions.HttpBaseUrl);
                // Prescribe enrich=true có thể chạy vài giây (RAG+LLM) — timeout rộng hơn Predict.
                http.Timeout = TimeSpan.FromSeconds(Math.Max(30, aiOptions.TimeoutSeconds));
            })
            .AddPolicyHandler(aiRetry);
        services.AddHttpClient<Implements.Ai.AiHealthHttpClient>((sp, http) =>
            {
                http.BaseAddress = new Uri(aiOptions.HttpBaseUrl);
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, aiOptions.TimeoutSeconds));
            })
            .AddPolicyHandler(aiRetry);

        // Composite fallback clients — cái được inject vào job.
        services.AddScoped<IAiPredictionClient, Implements.Ai.FallbackAiPredictionClient>();
        services.AddScoped<IAiPrescriptionClient, Implements.Ai.FallbackAiPrescriptionClient>();
        // Health: job đọc soc_mode + lfp_loaded từ đây thay vì hardcode theo chemistry.
        services.AddScoped<IAiHealthClient, Implements.Ai.FallbackAiHealthClient>();

        // Phản hồi prescription: proto nay ĐÃ có rpc SubmitFeedback, nên đường này cũng theo
        // khuôn gRPC primary → HTTP fallback như Predict/Prescribe. (Trước đây trỏ thẳng vào
        // bản HTTP vì proto chưa có RPC tương ứng — ràng buộc đó không còn.)
        services.AddScoped<Implements.Ai.AiPrescriptionFeedbackGrpcClient>();
        services.AddScoped<IAiPrescriptionFeedbackClient, Implements.Ai.FallbackAiPrescriptionFeedbackClient>();

        // F4 — phản hồi PHÂN LOẠI (khác phản hồi prescription ở trên: nhãn vs lời khuyên).
        // Chỉ gRPC, không fallback: phản hồi đã lưu vào DB trước khi gọi nên mất một lần
        // gửi chỉ làm chậm vòng học, không hỏng thao tác của người dùng.
        services.AddScoped<IAiClassificationFeedbackClient, Implements.Ai.AiClassificationFeedbackGrpcClient>();

        // C10 — dự đoán nhiều pin trong 1 kết nối (màn hình giám sát). Chỉ gRPC: REST không
        // có endpoint streaming tương ứng, nên cũng không có gì để fallback sang.
        services.AddScoped<IAiPredictionStreamClient, Implements.Ai.AiPredictionStreamGrpcClient>();

        // GH-10 — SOH chuỗi dài. Chỉ gRPC: REST của AI có /predict/long nhưng đường này
        // không nằm trên hot-path nên không cần fallback, thất bại thì báo 503.
        services.AddScoped<IAiPredictionLongClient, Implements.Ai.AiPredictionLongGrpcClient>();
        services.AddHostedService<SohPredictionBackgroundService>();

        // Sprint 5B B1 (#152) — NoiseBreachEvent retention 7 ngày.
        services.AddHostedService<NoiseBreachRetentionBackgroundService>();

        // Sprint BE-IoT-Realtime (#614..#623) — SSE telemetry (§34.10). Redis pub/sub, soft-dependency.
        services.Configure<RealtimeOptions>(configuration.GetSection(RealtimeOptions.SectionName));
        services.AddSingleton<ITelemetryPublisher, BatteryService.Infrastructure.Realtime.RedisTelemetryPublisher>();
        services.AddSingleton<ITelemetryStream, BatteryService.Infrastructure.Realtime.RedisTelemetryStream>();
        // Sprint Bonus NS-03/NS-04 (#648/#649) — rolling min/max nạp/xả streaming (event `stats`).
        services.AddSingleton<ITelemetryStatsService, BatteryService.Infrastructure.Realtime.RedisTelemetryStatsService>();
        // Sprint Bonus NS-06 (#650) — đọc continuous aggregate 1h (scoped, dùng ApplicationDbContext).
        services.AddScoped<ISensorReadingAggregateViewReader, BatteryService.Infrastructure.Realtime.SensorReadingAggregateViewReader>();
        services.AddScoped<IBatteryRealtimeAuthorizationService, BatteryService.Infrastructure.Implements.Services.BatteryRealtimeAuthorizationService>();

        // GH-722 — role của caller cho tầng REST, phục vụ giới hạn dữ liệu theo tenant.
        services.AddHttpContextAccessor();
        services.AddScoped<IBatteryCurrentUserService, BatteryService.Infrastructure.Implements.Services.BatteryCurrentUserService>();

        return services;
    }

    private static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BatteryDb")
                               ?? configuration["BatteryDb"]
                               ?? configuration["Battery_Db"]
                               ?? configuration["BATTERY_DB"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing Battery database connection string. Expected ConnectionStrings__BatteryDb, BatteryDb, Battery_Db, or BATTERY_DB.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
    }
}
