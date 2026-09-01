using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketAutoCreateFromAlertCommandHandler : IRequestHandler<TicketAutoCreateFromAlertCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCodeGenerator _codeGenerator;
    private readonly IPriorityCalculator _priorityCalculator;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-27
    private readonly ISlaCalculator _slaCalculator;

    public TicketAutoCreateFromAlertCommandHandler(
        ITicketUnitOfWork uow,
        ITicketCodeGenerator codeGenerator,
        IPriorityCalculator priorityCalculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher,
        ISlaCalculator slaCalculator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _priorityCalculator = priorityCalculator;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
        _slaCalculator = slaCalculator;
    }

    public async Task<TicketActionResponse> Handle(TicketAutoCreateFromAlertCommand request, CancellationToken ct)
    {
        var (impact, urgency) = MapAnomalyToB3(request.AnomalyCategory);
        var priority = _priorityCalculator.Calculate(impact, urgency);
        var code = await _codeGenerator.GenerateAsync();

        var ticket = new TicketEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = request.Title,
            Description = request.Description,
            Category = MapAnomalyToCategory(request.AnomalyCategory),
            CustomerId = request.CustomerId,
            BatteryAssetId = request.BatteryAssetId,
            Status = TicketStatusEnum.Open,
            Origin = IsEnvironmentalAnomaly(request.AnomalyCategory)
                ? TicketOriginEnum.AutoFromEnvironment
                : TicketOriginEnum.AutoFromAlert,
            OriginAlertId = request.OriginAlertId,
            // Thời điểm anomaly được phát hiện + serial pin — để FE hiện panel "Bằng chứng
            // cảnh báo (lúc phát hiện)" và cột Serial giống ticket do Customer tạo.
            DetectedAt = request.DetectedAt,
            BatterySerialNumber = request.BatterySerialNumber,
            // Site đi kèm event chứ KHÔNG gọi ngược BatteryService: handler này chạy từ
            // MassTransit consumer, không có HTTP context nên IBatteryLookupClient không forward
            // được JWT và sẽ luôn trả null. BatteryAnomalyDetectedV2Event vốn đã mang SiteId,
            // saga hydrate rồi forward qua CreateTicketFromAlertCommand — cùng khuôn
            // denormalize snapshot như BatterySerialNumber.
            //
            // Null khi alert đến từ event V1 (không có SiteId); khi đó ticket không gom theo site.
            SiteId = request.SiteId,
            ImpactScope = impact,
            UrgencyLevel = urgency,
            Priority = priority,
            IsIncident = impact >= ImpactScopeEnum.Site || request.AnomalyCategory == "EnvironmentalIncident"
        };

        await _uow.Tickets.AddAsync(ticket);

        var nowUtc = DateTime.UtcNow;
        var responseDueAt = _slaCalculator.CalculateResponseDueDate(nowUtc, priority);
        var slaTimer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = priority,
            Status = SlaTimerStatusEnum.Running,
            StartedAt = nowUtc,
            OriginalDueAt = responseDueAt,
            DueAt = responseDueAt
        };
        await _uow.SlaTimers.AddAsync(slaTimer);

        if (request.BatteryAssetId != Guid.Empty)
        {
            await _uow.TicketBatteryAssets.AddAsync(new TicketBatteryAsset
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                BatteryAssetId = request.BatteryAssetId
            });
        }

        await SaveAiSuggestionAsync(ticket.Id, request);

        // Dòng này hiện nguyên văn trên tab "Activity history" của FE, nên nó phải đọc được
        // bằng mắt người. Trước đây ghi thẳng OriginAlertId — một GUID không nói lên điều gì
        // với người đọc, mà tra ngược cũng không tiện vì màn Alerts tìm theo pin chứ không
        // theo id. Alert KHÔNG có mã dạng "TKT-…" để thay vào, nên mô tả bằng chính nội dung
        // của nó: loại bất thường + pin. Id vẫn nằm ở cột Ticket.OriginAlertId và ở
        // causation_id của bản ghi audit ngay dưới, nên không mất đường truy vết.
        var alertDescription = string.IsNullOrWhiteSpace(request.BatterySerialNumber)
            ? $"a {request.AnomalyCategory} alert"
            : $"a {request.AnomalyCategory} alert on {request.BatterySerialNumber}";

        await _activityLogger.LogAsync(
            ticket.Id,
            null,
            ActorRoleEnum.System,
            "System",
            ActivityActionEnum.Created,
            newValue: $"Auto-created from {alertDescription}");

        await _outboxWriter.WriteAsync(
            new TicketCreatedEvent(ticket.Id, ticket.Code, ticket.CustomerId, ticket.Priority?.ToString()), ct);

        // #AUDIT-27 — causation_id = OriginAlertId (anomaly event → ticket chain).
        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AutoCreatedFromAnomaly, ticket.Id, targetDisplay: ticket.Code,
            causationId: request.OriginAlertId), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Ticket auto-created from alert.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    /// <summary>
    /// Ghi gợi ý AI dạng có cấu trúc vào <c>ticket_ai_suggestions</c>.
    /// </summary>
    /// <remarks>
    /// BEST-EFFORT có chủ ý: mọi lỗi ở đây đều bị nuốt. Ticket là thứ bắt buộc phải tạo được
    /// (nó mở SLA và là đầu vào của cả quy trình xử lý); gợi ý AI chỉ là thông tin thêm.
    /// Đánh đổi ngược lại — để một lỗi ghi gợi ý làm hỏng việc tạo ticket từ alert — là
    /// không chấp nhận được.
    /// <para>
    /// Không ghi gì khi ticket đến từ threshold engine (<c>AiSuggestion == null</c>) hoặc
    /// payload rỗng — tránh tạo hàng loạt bản ghi trắng.
    /// </para>
    /// </remarks>
    private async Task SaveAiSuggestionAsync(Guid ticketId, TicketAutoCreateFromAlertCommand request)
    {
        var ai = request.AiSuggestion;
        if (ai is null || ai.IsEmpty)
            return;

        try
        {
            await _uow.TicketAiSuggestions.AddAsync(new TicketAiSuggestion
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                Prescription = request.AiPrescriptionText ?? string.Empty,
                ActionSteps = ai.ActionSteps?.ToList() ?? new List<string>(),
                PpeRequired = ai.PpeRequired?.ToList() ?? new List<string>(),
                SopReferences = ai.SopReferences?.ToList() ?? new List<string>(),
                EscalationConditions = ai.EscalationConditions?.ToList() ?? new List<string>(),
                SafetyWarnings = ai.SafetyWarnings?.ToList() ?? new List<string>(),
                KbDocRefs = ai.KbDocRefs?.ToList() ?? new List<string>(),
                HumanVerificationRequired = ai.HumanVerificationRequired ?? false,
                Blocked = ai.Blocked ?? false,
                Enriched = ai.Enriched ?? false,
                LlmProvider = ai.LlmProvider ?? "none",
                PrescriptionId = ai.PrescriptionId
            });
        }
        catch (Exception)
        {
            // Nuốt có chủ ý — xem <remarks> ở trên.
        }
    }

    /// <summary>
    /// Map loại anomaly → <see cref="TicketCategoryEnum"/>.
    ///
    /// FIX duplicate-detection: trước đây hardcode <c>Repair</c> cho MỌI ticket auto, nên
    /// ticket auto "Overheat" mang category Repair — không bao giờ khớp với ticket Customer
    /// chọn "Quá nhiệt" (Overheat) ⇒ AI dò trùng bỏ sót (không cộng điểm cùng category),
    /// và index ux_tickets_active_auto_per_asset_category cũng gom nhầm mọi loại anomaly
    /// vào chung 1 slot.
    ///
    /// Anomaly không map được sang category nghiệp vụ (DeviceOffline, SensorMismatch, …)
    /// vẫn về <c>Repair</c> — đúng bản chất "cần kỹ thuật viên xử lý".
    /// </summary>
    /// <summary>
    /// Anomaly này là sự cố MÔI TRƯỜNG của site, không phải bất thường của một viên pin.
    /// </summary>
    /// <remarks>
    /// Cùng danh sách với nhánh môi trường trong <see cref="MapAnomalyToCategory"/> và
    /// <c>MapAnomalyToB3</c> — ba chỗ phải khớp nhau, nếu không một loại anomaly sẽ mang origin
    /// môi trường mà lại có category/scope của pin.
    /// </remarks>
    public static bool IsEnvironmentalAnomaly(string anomalyCategory) => anomalyCategory
        is "HighAmbientTemp" or "HighHumidity" or "HighTempHumidityCombo"
        or "HighGasConcentration" or "EnvironmentalIncident" or "WaterLeak";

    public static TicketCategoryEnum MapAnomalyToCategory(string anomalyCategory) => anomalyCategory switch
    {
        // Nhiệt độ — cả hai chiều. Undertemp trước đây rơi vào `_ => Repair`, và vì
        // `ux_tickets_active_auto_per_asset_category` là UNIQUE trên (asset, category), nó
        // chiếm mất slot Repair của cả nhóm: alert Overvoltage ngay sau đó không insert được,
        // saga kẹt ở `TicketRequested` không log lỗi, và ticket biến mất trong im lặng.
        "Overheat" or "Undertemp"
            => TicketCategoryEnum.Overheat,

        // Sạc — dòng sạc bất thường và quá áp đều là sự cố của đường nạp.
        "AbnormalCharging" or "Overvoltage" => TicketCategoryEnum.Charging,

        "Undervoltage" or "LowSoc" => TicketCategoryEnum.NoPower,

        "SohDegradation" or "RapidDischarge" or "HighInternalResistance" or "CellImbalance"
            => TicketCategoryEnum.Performance,

        // Hạ tầng đo đạc/thiết bị — không phải lỗi của bản thân viên pin, nên tách khỏi
        // `Repair` để không tranh slot với các anomaly đo được ở trên.
        "SensorMismatch" or "DeviceOffline" or "IotDataIntegrityViolation"
            => TicketCategoryEnum.Other,

        // Su co MOI TRUONG cua site — hong hoc o tu/phong, khong phai o vien pin. Xep chung
        // `Repair` voi ticket do thiet bi tu bao (khoi, ro khi, ngap) de ca nhom doc nhat quan
        // tren hang doi. Truoc day `HighAmbientTemp` va `HighTempHumidityCombo` nam trong nhanh
        // `Overheat` — mot hang muc LOI PIN — nen ticket nhiet do cua ca site hien y het ticket
        // qua nhiet cua mot vien pin, du hai thu chang lien quan gi nhau.
        //
        // Khong anomaly nao cua pin xep vao `Repair`, nen o (asset, Repair) cua
        // `ux_tickets_active_auto_per_asset_category` la cua rieng nhom nay: gom lai day KHONG
        // tranh slot voi ticket pin, chi khien moi site co mot ticket moi truong dang mo tai
        // mot thoi diem — dung y nghia, vi do la mot su co cua mot cai tu.
        "HighAmbientTemp" or "HighHumidity" or "HighTempHumidityCombo"
            or "HighGasConcentration" or "EnvironmentalIncident" or "WaterLeak"
            => TicketCategoryEnum.Repair,

        // Con lai — can ky thuat vien toi xu ly.
        _ => TicketCategoryEnum.Repair
    };

    private static (ImpactScopeEnum, UrgencyLevelEnum) MapAnomalyToB3(string category)
    {
        return category switch
        {
            "EnvironmentalIncident" or "WaterLeak" => (ImpactScopeEnum.Site, UrgencyLevelEnum.High),
            "Overheat" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.High),

            // Cac anomaly do cam bien MOI TRUONG cua site sinh ra: hong hoc nam o tu/phong,
            // khong phai o mot vien pin. Truoc day chi `HighAmbientTemp` duoc xep Site, ba loai
            // con lai roi vao nhanh `_` thanh SingleAsset — ticket ro khi bi gan vao mot vien pin
            // va bi phan loai nhu su co pin. `ImpactScope = Site` cung la dau hieu ma
            // `TicketQueryHelper.FilterBySource` dung de nhan ra nhom nay.
            "HighAmbientTemp" or "HighHumidity" or "HighTempHumidityCombo" or "HighGasConcentration"
                => (ImpactScopeEnum.Site, UrgencyLevelEnum.Medium),
            "SohDegradation" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Low),
            "SensorMismatch" => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Medium),
            _ => (ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Medium)
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
