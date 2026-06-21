using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Expo push channel — HTTP POST tới <c>https://exp.host/--/api/v2/push/send</c>.
/// Polly retry 3 lần (exponential backoff) được cấu hình ở DI level (named client "expo").
/// Nếu Expo trả DeviceNotRegistered → deactivate token trong DB.
/// </summary>
public class ExpoPushChannel : INotificationChannel
{
    private const string ExpoUrl = "https://exp.host/--/api/v2/push/send";

    private readonly IHttpClientFactory _httpFactory;
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<ExpoPushChannel> _logger;

    public ExpoPushChannel(
        IHttpClientFactory httpFactory,
        INotificationUnitOfWork unitOfWork,
        ILogger<ExpoPushChannel> logger)
    {
        _httpFactory = httpFactory;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Push;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExpoToken))
        {
            _logger.LogWarning("ExpoPushChannel: no Expo token for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, "No Expo token");
        }

        var payload = new
        {
            to = request.ExpoToken,
            title = request.Title,
            body = request.Body,
            data = string.IsNullOrWhiteSpace(request.PayloadJson) ? null : JsonSerializer.Deserialize<object>(request.PayloadJson),
            sound = "default",
            priority = request.IsCritical ? "high" : "normal",
            channelId = request.IsCritical ? "alerts-critical" : "alerts-default"
        };

        try
        {
            var client = _httpFactory.CreateClient("expo");
            var response = await client.PostAsJsonAsync(ExpoUrl, payload, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ExpoResponse>(cancellationToken: ct);
            if (result?.Data is { } ticket)
            {
                if (ticket.Status == "error")
                {
                    var errorCode = ticket.Details?.Error;
                    _logger.LogWarning(
                        "ExpoPushChannel: Expo error {Error} for token {Token} notification {NotificationId}",
                        errorCode, request.ExpoToken, request.NotificationId);

                    if (errorCode == "DeviceNotRegistered")
                        await DeactivateTokenAsync(request.ExpoToken, ct);

                    return new ChannelResult(false, errorCode ?? "Expo error");
                }
            }

            return new ChannelResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExpoPushChannel: failed to send push for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, ex.Message);
        }
    }

    private async Task DeactivateTokenAsync(string token, CancellationToken ct)
    {
        var deviceToken = _unitOfWork.DeviceTokens
            .GetAllAsync()
            .Where(x => !x.IsDeleted && x.Token == token)
            .FirstOrDefault();

        if (deviceToken is null)
        {
            return;
        }

        deviceToken.IsActive = false;
        _unitOfWork.DeviceTokens.UpdateAsync(deviceToken);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // Expo Push API response shapes
    private sealed class ExpoResponse
    {
        [JsonPropertyName("data")]
        public ExpoTicket? Data { get; set; }
    }

    private sealed class ExpoTicket
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("details")]
        public ExpoTicketDetails? Details { get; set; }
    }

    private sealed class ExpoTicketDetails
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
