namespace BatteryService.Application.Import;

/// <summary>
/// Kiểm định và chuẩn hoá từng dòng của file nhập.
/// </summary>
/// <remarks>
/// <para>
/// Đây là bộ kiểm định <b>riêng</b> của đường nhập dữ liệu, cố tình tách khỏi bộ kiểm định của
/// đường tạo thủ công. Hai đường phục vụ hai việc khác nhau: tạo thủ công là một người nhập một
/// thiết bị vừa lắp, còn nhập hàng loạt là tiếp nhận lịch sử vận hành của cả một đơn vị lắp đặt.
/// Áp cùng một bộ ràng buộc thì hoặc là chặn oan dữ liệu thật, hoặc là phải nới lỏng cả đường
/// tạo thủ công — cả hai đều tệ hơn việc tách ra.
/// </para>
/// <para>
/// Bộ này gom <b>tất cả</b> lỗi của một dòng chứ không dừng ở lỗi đầu tiên: người dùng sửa file
/// một lần rồi nạp lại, chứ không sửa từng lỗi rồi nạp lại chục lần.
/// </para>
/// </remarks>
public interface IImportRowValidator
{
    ImportValidation<ValidatedCustomer> ValidateCustomer(ImportCustomerRow row);

    ImportValidation<ValidatedSite> ValidateSite(ImportSiteRow row);

    ImportValidation<ValidatedAsset> ValidateAsset(ImportAssetRow row);
}
