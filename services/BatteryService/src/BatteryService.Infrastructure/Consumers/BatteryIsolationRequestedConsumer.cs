using BatteryService.Application.CQRS.Command.BatteryAsset;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Manager bấm "Declare Incident" (hoặc SLA escalation đẩy ticket lên Urgent) → TicketService
/// publish <see cref="BatteryIsolationRequestedEvent"/>. Consumer này là chỗ sự cố biến thành
/// hành động vật lý: NGẮT MOSFET XẢ của mọi pin gắn với ticket qua gateway của site.
///
/// Chỉ ngắt xả, KHÔNG ngắt sạc — cắt luôn sạc thì pin không hồi được và cũng không cần thiết cho
/// việc cô lập tải. Bật lại là thao tác thủ công có chủ ý trên trang battery detail.
///
/// Idempotent theo hai lớp: handler trả 409 khi đã có lệnh cùng target đang chờ ack (redelivery
/// không đẻ lệnh trùng), và lệnh lặp lại chỉ ghi đè cùng một trạng thái MOSFET.
/// </summary>
public class BatteryIsolationRequestedConsumer : IConsumer<BatteryIsolationRequestedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<BatteryIsolationRequestedConsumer> _logger;

    public BatteryIsolationRequestedConsumer(
        IMediator mediator,
        ILogger<BatteryIsolationRequestedConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryIsolationRequestedEvent> context)
    {
        var msg = context.Message;
        if (msg.BatteryAssetIds is null || msg.BatteryAssetIds.Count == 0)
        {
            _logger.LogWarning(
                "Isolation requested for ticket {TicketId} but no battery asset is attached — nothing to cut.",
                msg.TicketId);
            return;
        }

        // Gom lỗi hạ tầng lại, cắt hết pin còn cắt được rồi mới ném: một site mất gateway không
        // được phép chặn việc ngắt xả những pin còn lại của cùng sự cố.
        var transportFailures = new List<string>();

        foreach (var assetId in msg.BatteryAssetIds.Distinct())
        {
            var result = await _mediator.Send(new SetBmsSwitchCommand
            {
                BatteryAssetId = assetId,
                Target = "discharge",
                Enable = false,
                // Guid.Empty (đường SLA tự động) → handler ghi audit null.
                IssuedByAccountId = msg.RequestedByAccountId
            }, context.CancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogWarning(
                    "Incident {EpisodeId} (ticket {TicketId}): discharge cut requested for asset {AssetId}, cmd {CmdId}.",
                    msg.IncidentEpisodeId, msg.TicketId, assetId, result.Data?.CmdId);
                continue;
            }

            // 409 = đã có lệnh cùng target đang chờ ack → đúng trạng thái mong muốn, không retry.
            if (result.StatusCode == 409)
            {
                _logger.LogInformation(
                    "Incident {EpisodeId}: asset {AssetId} already has a pending discharge command — skipped.",
                    msg.IncidentEpisodeId, assetId);
                continue;
            }

            // 503 = MQTT bridge chết. Đây là lỗi tạm thời và là hành động an toàn, phải retry.
            if (result.StatusCode == 503)
            {
                transportFailures.Add($"{assetId}: {result.Message}");
                continue;
            }

            // 404/400 — pin bị xoá, site chưa có gateway, hai gateway active. Retry không cứu được.
            _logger.LogError(
                "Incident {EpisodeId}: cannot cut discharge on asset {AssetId} — {Status} {Message}",
                msg.IncidentEpisodeId, assetId, result.StatusCode, result.Message);
        }

        if (transportFailures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Battery isolation for ticket {msg.TicketId} could not reach the MQTT bridge: "
                + string.Join("; ", transportFailures));
        }
    }
}
