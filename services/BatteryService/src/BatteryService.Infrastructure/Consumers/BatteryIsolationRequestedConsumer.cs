using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Manager bấm "Declare Incident" (hoặc SLA escalation đẩy ticket lên Urgent) → TicketService
/// publish <see cref="BatteryIsolationRequestedEvent"/>. Consumer này là chỗ sự cố biến thành
/// hành động vật lý: NGẮT MOSFET XẢ của mọi pin gắn với ticket qua gateway của site.
///
/// Chỉ ngắt xả, KHÔNG ngắt sạc — cắt luôn sạc thì pin không hồi được và cũng không cần thiết cho
/// việc cô lập tải. Bật lại là thao tác thủ công có chủ ý qua
/// <c>POST /environmental-incidents/{id}/bms-restore</c>, chỉ mở khi incident đã Resolved.
///
/// Ticket môi trường (khói/gas/ngập) KHÔNG gắn pin nào — sự cố nằm ở tủ chứ không ở một pack cụ
/// thể — nên <c>BatteryAssetIds</c> rỗng là hình dạng ĐÚNG của nó, không phải dữ liệu thiếu. Khi
/// đó pin được suy ra từ site của incident: cả site mất điện là phản ứng đúng cho sự cố môi
/// trường. Trước đây nhánh này chỉ log "nothing to cut" rồi thoát, tức là loại ticket cần ngắt
/// khẩn cấp nhất lại là loại duy nhất không ngắt được gì.
///
/// Idempotent theo hai lớp: handler trả 409 khi đã có lệnh cùng target đang chờ ack (redelivery
/// không đẻ lệnh trùng), và lệnh lặp lại chỉ ghi đè cùng một trạng thái MOSFET.
/// </summary>
public class BatteryIsolationRequestedConsumer : IConsumer<BatteryIsolationRequestedEvent>
{
    private readonly IMediator _mediator;
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ILogger<BatteryIsolationRequestedConsumer> _logger;

    public BatteryIsolationRequestedConsumer(
        IMediator mediator,
        IBatteryUnitOfWork unitOfWork,
        ILogger<BatteryIsolationRequestedConsumer> logger)
    {
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BatteryIsolationRequestedEvent> context)
    {
        var msg = context.Message;

        var assetIds = msg.BatteryAssetIds?.Distinct().ToList() ?? [];
        if (assetIds.Count == 0)
        {
            assetIds = await ResolveSiteAssetsAsync(msg, context.CancellationToken);
            if (assetIds.Count == 0) return;
        }

        // Gom lỗi hạ tầng lại, cắt hết pin còn cắt được rồi mới ném: một site mất gateway không
        // được phép chặn việc ngắt xả những pin còn lại của cùng sự cố.
        var transportFailures = new List<string>();

        foreach (var assetId in assetIds)
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

    /// <summary>
    /// Pin của một sự cố môi trường: mọi pack Active đứng trên site của incident.
    /// </summary>
    /// <remarks>
    /// Chỉ lấy <see cref="BatteryStatusEnum.Active"/>. Pin Inactive/Decommissioned không có BMS
    /// sống để trả lời, nên đưa vào chỉ tạo ra lệnh chắc chắn timeout — nhiễu log đúng lúc vận
    /// hành cần đọc log nhất.
    ///
    /// Không phân trang: đây là toàn bộ site, và một danh sách bị cắt trang sẽ báo cáo ngắt
    /// thành công trong khi vẫn còn pin đang cấp điện — đúng kiểu hỏng mà một điều khiển an toàn
    /// không được phép có.
    /// </remarks>
    private async Task<List<Guid>> ResolveSiteAssetsAsync(
        BatteryIsolationRequestedEvent msg,
        CancellationToken ct)
    {
        var incident = await _unitOfWork.EnvironmentalIncidents.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == msg.IncidentEpisodeId && !x.IsDeleted, ct);

        if (incident is null)
        {
            _logger.LogWarning(
                "Isolation requested for ticket {TicketId} with no battery attached, and incident "
                + "{EpisodeId} was not found — nothing to cut.",
                msg.TicketId, msg.IncidentEpisodeId);
            return [];
        }

        var assetIds = await _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(asset => !asset.IsDeleted
                            && asset.SiteId == incident.SiteId
                            && asset.Status == BatteryStatusEnum.Active)
            .Select(asset => asset.Id)
            .ToListAsync(ct);

        if (assetIds.Count == 0)
        {
            _logger.LogWarning(
                "Incident {EpisodeId} (ticket {TicketId}): site {SiteId} has no active battery — "
                + "nothing to cut.",
                msg.IncidentEpisodeId, msg.TicketId, incident.SiteId);
            return [];
        }

        _logger.LogWarning(
            "Incident {EpisodeId} (ticket {TicketId}): environmental ticket carries no battery — "
            + "cutting discharge on all {Count} active batteries at site {SiteId}.",
            msg.IncidentEpisodeId, msg.TicketId, assetIds.Count, incident.SiteId);

        return assetIds;
    }
}
