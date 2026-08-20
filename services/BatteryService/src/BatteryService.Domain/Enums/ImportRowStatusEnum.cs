namespace BatteryService.Domain.Enums;

/// <summary>Trạng thái của từng dòng trong lô import.</summary>
public enum ImportRowStatusEnum
{
    /// <summary>Chưa kiểm tra.</summary>
    Pending = 1,

    /// <summary>Kiểm tra đạt, sẵn sàng ghi.</summary>
    Valid = 2,

    /// <summary>Kiểm tra không đạt — chi tiết nằm ở <c>ErrorsJson</c>.</summary>
    Invalid = 3,

    /// <summary>Đã yêu cầu cấp tài khoản, đang chờ bản sao khách hàng.</summary>
    AwaitingAccount = 4,

    /// <summary>Đã tạo bản ghi mới.</summary>
    Created = 5,

    /// <summary>Đã cập nhật bản ghi có sẵn (nhận ra qua bản đồ liên kết).</summary>
    Updated = 6,

    /// <summary>Bỏ qua có chủ ý — ví dụ email khách đã tồn tại nên chỉ liên kết.</summary>
    Skipped = 7,

    /// <summary>Ghi thất bại ở pha 2.</summary>
    Failed = 8,

    /// <summary>Đã bị hoàn tác.</summary>
    Reverted = 9
}
