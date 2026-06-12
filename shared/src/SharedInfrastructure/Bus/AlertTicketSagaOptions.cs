namespace SharedInfrastructure.Bus;

/// <summary>
/// Feature flags cho cutover từ direct <c>BatteryAnomalyDetectedConsumer</c>
/// sang Saga orchestration.
///
/// Sprint 5B #238 (xem overall.md §53.7 cutover):
/// - <see cref="AlertTicketDispatchEnabled"/> = false trong maintenance window khi cần
///   pause ingestion mới (cả direct lẫn Saga đều skip).
/// - <see cref="AlertTicketSagaEnabled"/> = true sau khi Saga endpoint stable.
///   Khi true, direct consumer KHÔNG được register vào endpoint.
///
/// **Không cho phép** cả Saga và direct consumer chạy song song với cùng event
/// (tạo Ticket trùng). Cutover sequence:
///   1. <c>AlertTicketSagaEnabled=true</c> + <c>AlertTicketDispatchEnabled=true</c> → only Saga handles.
///   2. Direct consumer được mark [Obsolete] + remove khỏi endpoint registration.
/// </summary>
public class AlertTicketSagaOptions
{
    public const string SectionName = "AlertTicketSaga";

    /// <summary>
    /// Master switch: nếu false → cả Saga và direct consumer đều ignore event
    /// (maintenance window). Default true.
    /// </summary>
    public bool AlertTicketDispatchEnabled { get; set; } = true;

    /// <summary>
    /// Saga endpoint enabled? Khi true direct consumer KHÔNG register.
    /// Default true sau Sprint 5B cutover.
    /// </summary>
    public bool AlertTicketSagaEnabled { get; set; } = true;
}
