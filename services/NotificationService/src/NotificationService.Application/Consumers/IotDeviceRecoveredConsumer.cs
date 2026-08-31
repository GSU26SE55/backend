using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Closes the user-visible lifecycle of an IoT outage. Recovery is emitted only after the
/// BatteryService debounce policy accepts consecutive healthy signals and resolves the incident.
/// </summary>
public sealed class IotDeviceRecoveredConsumer : IConsumer<IotDeviceRecoveredEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<IotDeviceRecoveredConsumer> _logger;

    public IotDeviceRecoveredConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<IotDeviceRecoveredConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IotDeviceRecoveredEvent> context)
    {
        await NotificationDebounce.ProcessOnceAsync(
            _cache,
            context,
            nameof(IotDeviceRecoveredEvent),
            _logger,
            async () =>
            {
                var evt = context.Message;
                await NotificationDebounce.ProcessOnceByBusinessKeyAsync(
                    _cache,
                    "iot-recovered",
                    evt.AlertId ?? evt.IotDeviceId,
                    TimeSpan.FromDays(7),
                    context.CancellationToken,
                    async () =>
                    {
                        var recipients = (await _recipientResolver.GetActiveByRoleAsync(
                                context.CancellationToken, "Manager", "Admin"))
                            .Where(id => id != Guid.Empty)
                            .ToList();
                        if (evt.CustomerId is { } customerId && customerId != Guid.Empty)
                            recipients.Add(customerId);
                        recipients = recipients.Distinct().ToList();

                        if (recipients.Count == 0)
                        {
                            _logger.LogWarning(
                                "No recipient resolved for IoT recovery device={DeviceId}",
                                evt.IotDeviceId);
                            return;
                        }

                        var site = evt.SiteName ?? evt.SiteId.ToString();
                        var payload = JsonSerializer.Serialize(new
                        {
                            iotDeviceId = evt.IotDeviceId,
                            deviceCode = evt.DeviceCode,
                            siteId = evt.SiteId,
                            alertId = evt.AlertId,
                            recoveredAt = evt.RecoveredAt,
                            lastOfflineAt = evt.LastOfflineAt
                        });

                        await NotificationWriter.WriteAsync(
                            _unitOfWork,
                            recipients,
                            NotificationTypeEnum.IotDeviceRecovered,
                            NotificationWriter.InAppPush,
                            $"[IoT] Device recovered — {evt.DeviceCode}",
                            $"Device \"{evt.DisplayName}\" at site \"{site}\" is stable again. The offline incident has been resolved.",
                            payload,
                            "IotDevice",
                            evt.IotDeviceId,
                            context.CancellationToken);
                    });
            });
    }
}
