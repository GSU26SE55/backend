namespace BatteryService.Domain.Enums;

/// <summary>Loại dữ liệu mà một dòng import mô tả.</summary>
/// <remarks>
/// Thứ tự giá trị cũng chính là thứ tự phụ thuộc khi ghi: khách hàng trước, rồi site, rồi pin.
/// Khi hoàn tác thì đi ngược lại.
/// <para>
/// Thiết bị IoT KHÔNG nằm ở đây, và đó là quyết định có chủ ý: thiết bị do hệ thống cấp phát cùng
/// khoá API và credential MQTT, nên bên thứ ba không thể khai sinh một thiết bị. Thứ họ biết chỉ là
/// gateway nào đã lắp ở site nào — việc đó thuộc màn quản trị thiết bị, không thuộc luồng nhập dữ liệu.
/// </para>
/// </remarks>
public enum ImportEntityTypeEnum
{
    Customer = 1,
    Site = 2,
    BatteryAsset = 3
}
