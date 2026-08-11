using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>Pages operations when invalid readings force an IoT device into a safe disabled state.</summary>
public sealed class IotDeviceAutoDecommissionedConsumer : IConsumer<IotDeviceAutoDecommissionedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<IotDeviceAutoDecommissionedConsumer> _logger;

    public IotDeviceAutoDecommissionedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<IotDeviceAutoDecommissionedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IotDeviceAutoDecommissionedEvent> context)
    {
        await NotificationDebounce.ProcessOnceAsync(
            _cache,
            context,
            nameof(IotDeviceAutoDecommissionedEvent),
            _logger,
            async () =>
            {
                var evt = context.Message;
                await NotificationDebounce.ProcessOnceByBusinessKeyAsync(
                    _cache,
                    "iot-auto-decommissioned",
                    evt.AlertId,
                    TimeSpan.FromDays(30),
                    context.CancellationToken,
                    async () =>
                    {
                        var recipients = await _recipientResolver.GetActiveByRoleAsync(
                            context.CancellationToken, "Admin", "Manager");
                        if (recipients.Count == 0)
                        {
                            _logger.LogWarning(
                                "No Admin/Manager recipient resolved for auto-decommissioned device={DeviceId}",
                                evt.IotDeviceId);
                            return;
                        }

                        var payload = JsonSerializer.Serialize(new
                        {
                            iotDeviceId = evt.IotDeviceId,
                            deviceCode = evt.DeviceCode,
                            siteId = evt.SiteId,
                            alertId = evt.AlertId,
                            rejectedReadingCount = evt.RejectedReadingCount,
                            windowStartedAt = evt.WindowStartedAt,
                            decommissionedAt = evt.DecommissionedAt
                        });

                        await NotificationWriter.WriteAsync(
                            _unitOfWork,
                            recipients,
                            NotificationTypeEnum.IotDeviceAutoDecommissioned,
                            NotificationWriter.InAppPush,
                            $"[IoT] Device disabled — {evt.DeviceCode}",
                            $"Device \"{evt.DisplayName}\" was disabled after {evt.RejectedReadingCount} invalid readings. Inspect data integrity and credentials before reactivation.",
                            payload,
                            "IotDevice",
                            evt.IotDeviceId,
                            context.CancellationToken);
                    });
            });
    }
}
