using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Metrics;

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-08 (#708) — giám sát dead-letter queue.
///
/// MassTransit tự chuyển message vào queue <c>&lt;tên-queue&gt;_error</c> khi consumer thất bại hết
/// lượt retry. Trước sprint này KHÔNG AI theo dõi các queue đó: message chết nằm im, không log,
/// không cảnh báo. Tài liệu vận hành coi "DLQ = 0" là trạng thái khoẻ mạnh và bất kỳ message nào
/// xuất hiện cũng phải điều tra — muốn vậy thì trước hết phải ĐO được.
///
/// Không có API nào của MassTransit đọc được độ sâu queue, nên worker này hỏi RabbitMQ Management
/// API (<c>GET /api/queues</c>) và đẩy vào gauge <c>notification_dlq_size</c>; alert
/// <c>NotificationDlqNotEmpty</c> bắn khi tổng &gt; 0.
///
/// Chỉ giám sát, KHÔNG tự xử lý message — quyết định replay hay bỏ là việc của con người.
/// </summary>
public class NotificationDlqMonitorBackgroundService : BackgroundService
{
    private const string HttpClientName = "rabbitmq-management";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationDlqMonitorBackgroundService> _logger;

    public NotificationDlqMonitorBackgroundService(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<NotificationDlqMonitorBackgroundService> logger)
    {
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private bool Enabled =>
        _configuration.GetValue<bool?>("MessageBus:DlqMonitor:Enabled") ?? true;

    private TimeSpan Interval =>
        TimeSpan.FromSeconds(Math.Max(10, _configuration.GetValue<int?>("MessageBus:DlqMonitor:IntervalSeconds") ?? 60));

    /// <summary>
    /// Base URL của Management API. Mặc định suy ra từ <c>RabbitMQ:Host</c> với cổng quản trị 15672
    /// (đúng cổng docker-compose expose).
    /// </summary>
    private string ManagementUrl
    {
        get
        {
            var configured = _configuration["MessageBus:DlqMonitor:ManagementUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.TrimEnd('/');

            var host = _configuration["RabbitMQ:Host"] ?? "localhost";
            // RabbitMQ:Host có thể là "rabbitmq", "amqp://rabbitmq" hoặc "rabbitmq:5672".
            host = host.Replace("amqp://", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('/');
            var slash = host.IndexOf('/');
            if (slash >= 0)
                host = host[..slash];
            var colon = host.IndexOf(':');
            if (colon >= 0)
                host = host[..colon];

            return $"http://{host}:15672";
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            _logger.LogInformation("NotificationDlqMonitor bị tắt qua MessageBus:DlqMonitor:Enabled=false.");
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        _logger.LogInformation("NotificationDlqMonitor started (url={Url}, interval={Interval}).",
            ManagementUrl, Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }

            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Management API không sẵn sàng KHÔNG được làm chết service — chỉ mất khả năng đo.
                _logger.LogWarning(ex, "NotificationDlqMonitor: không đọc được RabbitMQ Management API.");
            }
        }

        _logger.LogInformation("NotificationDlqMonitor stopped.");
    }

    /// <summary>Đọc 1 lần và cập nhật gauge. Public để test gọi trực tiếp.</summary>
    public async Task<IReadOnlyDictionary<string, long>> PollAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient(HttpClientName);

        var user = _configuration["RabbitMQ:Username"] ?? "guest";
        var pass = _configuration["RabbitMQ:Password"] ?? "guest";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

        using var response = await client.GetAsync($"{ManagementUrl}/api/queues", ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(ct);
        var queues = JsonSerializer.Deserialize<List<RabbitQueue>>(payload) ?? [];

        var errorQueues = queues
            .Where(q => !string.IsNullOrEmpty(q.Name) && q.Name.EndsWith("_error", StringComparison.Ordinal))
            .ToDictionary(q => q.Name!, q => q.Messages);

        // Ghi cả queue rỗng: nếu chỉ ghi khi > 0 thì gauge kẹt ở giá trị cũ sau khi đã dọn xong DLQ.
        foreach (var (name, count) in errorQueues)
            AppMetrics.NotificationDlqSize.WithLabels(name).Set(count);

        var total = errorQueues.Values.Sum();
        if (total > 0)
        {
            _logger.LogWarning(
                "DLQ có {Total} message trên {Count} queue: {Queues}. Trạng thái khoẻ mạnh là 0 — cần điều tra.",
                total, errorQueues.Count(q => q.Value > 0),
                string.Join(", ", errorQueues.Where(q => q.Value > 0).Select(q => $"{q.Key}={q.Value}")));
        }

        return errorQueues;
    }

    /// <summary>Phần payload của <c>GET /api/queues</c> mà worker này cần.</summary>
    private sealed class RabbitQueue
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("messages")]
        public long Messages { get; set; }
    }
}
