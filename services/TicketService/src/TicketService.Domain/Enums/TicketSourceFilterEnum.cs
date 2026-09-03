namespace TicketService.Domain.Enums;

/// <summary>
/// Bộ lọc "nguồn tạo ticket". KHÔNG phải cột thật trong DB —
/// <see cref="TicketOriginEnum"/> mới là cột lưu, nhưng một mình nó không đủ:
/// sự cố môi trường, bảo trì định kỳ, và cascade risk đều ghi <c>Origin = System</c>,
/// nên lọc theo Origin thì gộp ba luồng khác hẳn nhau làm một.
///
/// Năm nguồn người dùng phân biệt trên UI. Xét field chuyên biệt trước rồi mới tới
/// Origin — cùng thứ tự ưu tiên FE dùng để gắn nhãn.
/// </summary>
public enum TicketSourceFilterEnum
{
    /// <summary>Khách tự tạo (Origin = ManualByCustomer).</summary>
    Customer = 1,

    /// <summary>
    /// AI dự đoán ra: sinh từ alert bất thường mà AI module chấm (Origin = AutoFromAlert,
    /// qua TicketBatteryAnomalyDetectedConsumer). KHÔNG bao gồm cascade risk — đó là công
    /// thức rule-based cộng điểm cứng, không có ML tham gia, xem <see cref="CascadeRisk"/>.
    /// </summary>
    AiPredicted = 2,

    /// <summary>Sự cố môi trường tại site — nhận diện bằng EnvironmentalIncidentId.</summary>
    Environmental = 3,

    /// <summary>
    /// Bảo trì định kỳ của pin — nhận diện bằng PeriodicMaintenanceDueAtUtc.
    /// KHÔNG dùng PeriodicMaintenanceSourceTicketId: từ khi lịch bảo trì chuyển sang
    /// tầng tài sản, field đó luôn trống nên điều kiện sẽ không bao giờ khớp.
    /// </summary>
    PeriodicMaintenance = 4,

    /// <summary>
    /// Rủi ro lan truyền (cascade risk) cao — ticket tự tạo/nâng P1 do
    /// TicketBatteryCascadeRiskHighConsumer (Origin = System, không kèm sự cố môi trường /
    /// hạn bảo trì). Tách khỏi <see cref="AiPredicted"/> — trước gộp chung dễ khiến người
    /// đọc hiểu nhầm cascade risk có ML tham gia, trong khi đây là rule-based thuần.
    /// </summary>
    CascadeRisk = 5
}
