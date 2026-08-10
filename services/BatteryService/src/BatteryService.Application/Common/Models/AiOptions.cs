namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — config cho AI bridge (SohPredictionBackgroundService gọi AI module).
/// Transport: gRPC primary (<see cref="GrpcAddress"/>) → HTTP fallback (<see cref="HttpBaseUrl"/>).
/// <see cref="Enabled"/>=false → job no-op hoàn toàn (threshold rule vẫn chạy).
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Bật/tắt toàn bộ AI bridge. false → job không gọi AI, không insert prediction.
    /// Mặc định false (fail-safe): local/non-docker không có ai-module → job no-op.
    /// Docker bật qua env <c>Ai__Enabled</c> / <c>AI_ENABLED</c> (xem .env.Docker + docker-compose).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>gRPC endpoint (primary). VD "http://ai-module-grpc:50051" — insecure, nội bộ network.</summary>
    public string GrpcAddress { get; set; } = "http://localhost:50051";

    /// <summary>HTTP/FastAPI endpoint (fallback). VD "http://ai-module-http:8000".</summary>
    public string HttpBaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Timeout mỗi call AI (giây). SLA inference &lt; 100ms nên 5s là dư cho cả cold start.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Chu kỳ job gọi AI (phút). KHÔNG gọi mỗi reading (5s) — gom lại mỗi N phút.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// GH-780 — số dòng payload gửi AI. HẰNG SỐ, KHÔNG cấu hình được.
    /// </summary>
    /// <remarks>
    /// AI từ chối thẳng mọi payload khác 30 dòng
    /// (<c>predict.py</c>: <c>if len(v) != WINDOW_SIZE: raise</c>) vì 30 là hình dạng cửa sổ đã nướng
    /// vào chính trọng số model — không phải tham số vận hành. Trước đây <see cref="MinReadings"/>
    /// đóng CẢ HAI vai: vừa là ngưỡng "đủ lịch sử chưa", vừa là số dòng gửi đi. Đặt
    /// <c>Ai:MinReadings = 29</c> hoặc <c>31</c> là qua được ngưỡng rồi gửi payload sai hình dạng ⇒
    /// REST trả 422, gRPC trả INVALID_ARGUMENT, worker nhận null và prediction DỪNG HẲN — mà nhìn
    /// cấu hình thì không thấy gì sai.
    /// </remarks>
    public const int WindowSize = 30;

    /// <summary>
    /// Số reading tối thiểu/pin mới bắt đầu dự đoán (ngưỡng "đủ lịch sử").
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="WindowSize"/> (GH-780). Phải ≥ <see cref="WindowSize"/> — đòi ít hơn 30
    /// mẫu thì đến lúc gửi vẫn không đủ 30 dòng để dựng payload. Ràng buộc này được kiểm lúc KHỞI
    /// ĐỘNG (xem ManageDependencyInjection) để cấu hình sai làm service không lên, thay vì lên
    /// bình thường rồi câm lặng.
    /// </remarks>
    public int MinReadings { get; set; } = WindowSize;

    /// <summary>
    /// GH-762 — số reading quét về để còn chỗ BÙ sau khi loại số đo ngoài dải AI.
    /// </summary>
    /// <remarks>
    /// Trước đây job lấy đúng <see cref="MinReadings"/>; chỉ một số đo bất khả thi (vd 52.4 V
    /// trên pack 12 V) là AI từ chối nguyên cửa sổ và pin đó không có prediction nào cho tới khi
    /// outlier tự rơi khỏi 30 mẫu. Quét dư cho phép thay thế mẫu hỏng bằng mẫu cũ hơn còn tốt.
    /// <para>
    /// Không đặt quá lớn: cửa sổ càng lùi xa thì càng ít phản ánh hiện trạng pin. Gấp đôi là đủ
    /// để chịu được một chuỗi outlier ngắn mà vẫn giữ cửa sổ trong tầm gần.
    /// </para>
    /// </remarks>
    public int MaxScanReadings { get; set; } = 60;

    /// <summary>Bật gọi /prescribe (enrich=true) khi Alert P1/P2. false → chỉ Predict.</summary>
    public bool PrescriptionEnabled { get; set; } = true;
}
