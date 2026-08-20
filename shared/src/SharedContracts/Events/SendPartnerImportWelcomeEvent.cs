using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Yêu cầu EmailService gửi thư chào mừng cho khách hàng vừa được tạo tài khoản từ dữ liệu import
/// của bên thứ ba.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao cần thư riêng thay vì tái dùng thư mời quản trị:</b> thư mời dẫn tới màn chấp nhận lời
/// mời, mà màn đó chỉ chạy với tài khoản đang chờ xác minh. Tài khoản import được tạo thẳng ở trạng
/// thái hoạt động nên đường đó luôn báo lỗi.
/// </para>
/// <para>
/// Mật khẩu sinh ngẫu nhiên lúc tạo tài khoản <b>không</b> được gửi qua thư. Thư chỉ dẫn khách sang
/// màn "Quên mật khẩu" để tự đặt mật khẩu — luồng này chỉ đòi tài khoản đang hoạt động nên dùng được
/// ngay, và tránh việc mật khẩu nằm trong hộp thư của khách.
/// </para>
/// </remarks>
/// <param name="AccountId">Tài khoản vừa được cấp.</param>
/// <param name="Email">Địa chỉ nhận thư.</param>
/// <param name="FullName">Họ tên để xưng hô trong thư.</param>
public record SendPartnerImportWelcomeEvent(
    Guid AccountId,
    string Email,
    string FullName
) : IntegrationEvent;
