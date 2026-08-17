using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Saga.AlertTicket;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// TicketService consumer cho <see cref="CreateTicketFromAlertCommand"/> từ Saga.
///
/// Idempotent: nếu đã có Ticket active cho cùng (BatteryAssetId, AnomalyCategory)
/// thì reuse (response IsReused=true). Nếu OriginAlertId đã có Ticket (redelivery)
/// thì cũng reuse.
///
/// Sprint 5B #238 — thay direct <c>TicketBatteryAnomalyDetectedConsumer</c> (xem overall.md §53.7).
/// </summary>
public class CreateTicketFromAlertConsumer : IConsumer<CreateTicketFromAlertCommand>
{
    private readonly IMediator _mediator;
    private readonly ITicketUnitOfWork _uow;
    private readonly ILogger<CreateTicketFromAlertConsumer> _logger;

    // PHẢI khớp index ux_tickets_active_auto_per_asset_category (status NOT IN 6,7,8 —
    // Completed/Closed/ClosedRejected, theo bảng mã GH-1176).
    // Lệch danh sách này với index → idempotency check cho qua rồi INSERT vỡ 23505.
    private static readonly TicketStatusEnum[] ActiveStatuses =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Pending,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.Request,
        TicketStatusEnum.ReAssign
    };

    public CreateTicketFromAlertConsumer(
        IMediator mediator,
        ITicketUnitOfWork uow,
        ILogger<CreateTicketFromAlertConsumer> logger)
    {
        _mediator = mediator;
        _uow = uow;
        _logger = logger;
    }

    /// <summary>
    /// Postgres 23505 = unique_violation. Tùy đường chạy, EF có thể ném thẳng
    /// <see cref="Npgsql.PostgresException"/> hoặc bọc trong <see cref="DbUpdateException"/>
    /// → check cả 2 (và cả inner lồng nhau).
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is Npgsql.PostgresException { SqlState: "23505" })
                return true;
        }
        return false;
    }

    public async Task Consume(ConsumeContext<CreateTicketFromAlertCommand> context)
    {
        var msg = context.Message;

        // Idempotency #1: redelivery cùng OriginAlertId.
        var existingByAlert = await _uow.Tickets.GetAllAsync()
            .Where(t => t.OriginAlertId == msg.AlertId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Code })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existingByAlert is not null)
        {
            _logger.LogInformation(
                "Saga {Correlation}: Ticket already exists for AlertId {AlertId} (redelivery). Returning existing.",
                msg.CorrelationId, msg.AlertId);

            await context.Publish(new TicketCreatedFromAlertResponse(
                msg.CorrelationId, msg.AlertId, existingByAlert.Id, existingByAlert.Code, IsReused: true));
            return;
        }

        // Idempotency #2: reuse active Ticket cùng (BatteryAssetId, AnomalyCategory).
        // Dùng CHUNG map với handler — trước đây Enum.TryParse("SohDegradation") fail →
        // fallback Other, trong khi handler tạo ticket với category khác ⇒ check lệch,
        // không tìm thấy ticket đã có ⇒ tạo trùng rồi vỡ unique index.
        var category = TicketAutoCreateFromAlertCommandHandler.MapAnomalyToCategory(msg.AnomalyCategory);

        var existingActive = await _uow.Tickets.GetAllAsync()
            .Where(t =>
                t.BatteryAssetId == msg.BatteryAssetId &&
                t.Category == category &&
                t.OriginAlertId != null &&
                ActiveStatuses.Contains(t.Status) &&
                !t.IsDeleted)
            .Select(t => new { t.Id, t.Code })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existingActive is not null)
        {
            _logger.LogInformation(
                "Saga {Correlation}: Reusing active Ticket {TicketId} for asset/category match.",
                msg.CorrelationId, existingActive.Id);

            await context.Publish(new TicketCreatedFromAlertResponse(
                msg.CorrelationId, msg.AlertId, existingActive.Id, existingActive.Code, IsReused: true));
            return;
        }

        // Create new Ticket qua MediatR command (reuse business logic existing).
        var createCmd = new TicketAutoCreateFromAlertCommand
        {
            OriginAlertId = msg.AlertId,
            BatteryAssetId = msg.BatteryAssetId,
            CustomerId = msg.CustomerId,
            AnomalyCategory = msg.AnomalyCategory,
            Title = msg.Title,
            Description = msg.Description,
            DetectedAt = msg.DetectedAt,
            BatterySerialNumber = msg.AssetSerialNumber,
            // BE-AI structured — gói lại để handler ghi `ticket_ai_suggestions`. Null với
            // ticket từ threshold engine (không gọi AI) và với command do saga cũ publish.
            AiPrescriptionText = msg.AiPrescription,
            AiSuggestion = new AiSuggestionPayload(
                ActionSteps: msg.AiActionSteps,
                PpeRequired: msg.AiPpeRequired,
                SopReferences: msg.AiSopReferences,
                EscalationConditions: msg.AiEscalationConditions,
                SafetyWarnings: msg.AiSafetyWarnings,
                KbDocRefs: msg.AiKbDocRefs,
                HumanVerificationRequired: msg.AiHumanVerificationRequired,
                Blocked: msg.AiBlocked,
                Enriched: msg.AiEnriched,
                LlmProvider: msg.AiLlmProvider,
                PrescriptionId: msg.AiPrescriptionId)
        };

        TicketService.Application.DTOs.Response.Tickets.TicketActionResponse result;
        try
        {
            result = await _mediator.Send(createCmd, context.CancellationToken);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // FIX race-condition: 2 alert cùng pin/category sinh gần như đồng thời → 2 saga →
            // 2 command chạy song song, cả hai qua được idempotency check rồi cùng INSERT.
            // Constraint ux_tickets_active_auto_per_asset_category (hoặc IX_tickets_code) bắn 23505.
            // Người thắng đã tạo ticket → đọc lại và reuse thay vì fault vào error queue.
            // Tìm ticket auto active của cùng pin — KHÔNG lọc theo `category` parse từ message
            // vì handler set Category = Repair cố định, lệch với AnomalyCategory.
            var winner = await _uow.Tickets.GetAllAsync()
                .AsNoTracking()
                .Where(t =>
                    t.BatteryAssetId == msg.BatteryAssetId &&
                    t.OriginAlertId != null &&
                    ActiveStatuses.Contains(t.Status) &&
                    !t.IsDeleted)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Code })
                .FirstOrDefaultAsync(context.CancellationToken);

            if (winner is not null)
            {
                _logger.LogInformation(
                    "Saga {Correlation}: Ticket đã được message song song tạo ({Code}) — reuse.",
                    msg.CorrelationId, winner.Code);

                await context.Publish(new TicketCreatedFromAlertResponse(
                    msg.CorrelationId, msg.AlertId, winner.Id, winner.Code, IsReused: true));
                return;
            }

            // Không tìm được ticket "người thắng" → để MassTransit retry.
            throw;
        }

        if (!result.IsSuccess || result.Data is null)
        {
            var reason = result.Message ?? "Ticket creation failed";
            var errorCode = result.ListErrors.FirstOrDefault()?.Field;

            _logger.LogWarning(
                "Saga {Correlation}: Ticket creation rejected. Reason={Reason}, ErrorCode={ErrorCode}",
                msg.CorrelationId, reason, errorCode);

            await context.Publish(new TicketCreationFromAlertRejected(
                msg.CorrelationId, msg.AlertId, reason, errorCode));
            return;
        }

        // FIX null-ticket-id: TicketActionDTO có cả Id (luôn set) lẫn TicketId (nullable).
        // TicketAutoCreateFromAlertCommandHandler chỉ set Id → Guid.Parse(TicketId) ném
        // ArgumentNullException SAU KHI ticket đã được tạo → saga không nhận response,
        // phải chờ retry rồi mới đi nhánh reuse. Ưu tiên TicketId, fallback Id.
        var createdTicketId = result.Data.TicketId ?? result.Data.Id;

        await context.Publish(new TicketCreatedFromAlertResponse(
            msg.CorrelationId,
            msg.AlertId,
            Guid.Parse(createdTicketId),
            result.Data.Code,
            IsReused: false));
    }
}
