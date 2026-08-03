using System.Text.Json;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Templates;

/// <summary>
/// Quy dữ liệu mẫu (JSON tuỳ ý client gửi) về dictionary phẳng để Handlebars đọc được.
/// Dùng chung cho <c>preview</c> và <c>test-send</c> — hai đường phải render ra kết quả GIỐNG HỆT,
/// nếu mỗi bên tự dựng model thì "xem trước thấy đúng nhưng gửi đi lại khác" là lỗi rất khó truy.
/// </summary>
public static class TemplateSampleModel
{
    /// <summary>
    /// Như <see cref="Build"/> nhưng nạp sẵn <b>đúng những khoá mà consumer của
    /// <paramref name="type"/> thật sự ghi</b>, mỗi khoá mang giá trị mẫu <c>«tênKhoá»</c>.
    ///
    /// <para><b>Vì sao cần.</b> <see cref="Build"/> chỉ nhận dữ liệu mẫu do client tự gõ. Người soạn
    /// gõ mẫu <c>{"ticketCode":"TK-1"}</c> thì xem trước hiện ra đẹp đẽ, trong khi lúc gửi thật
    /// consumer ghi khoá <c>code</c> nên biến render ra rỗng. Xem trước "thấy đúng nhưng gửi đi lại
    /// khác" chính là cách bộ template sai tên biến sống sót qua nhiều tháng. Nạp mặc định theo
    /// danh mục thì biến nào không có thật sẽ hiện rỗng ngay trên màn hình xem trước.</para>
    ///
    /// <para>Dữ liệu client gửi vẫn được ưu tiên: nó ghi đè lên giá trị mẫu, để ai muốn thử nội dung
    /// cụ thể vẫn thử được.</para>
    /// </summary>
    public static Dictionary<string, object?> BuildFor(
        NotificationTypeEnum type, JsonElement? sampleData)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in NotificationTemplateVariables.PayloadKeysFor(type))
            model[key] = $"«{key}»";

        foreach (var (key, value) in Build(sampleData))
            model[key] = value;

        return model;
    }

    /// <summary>
    /// Không gửi gì (hoặc gửi thứ không phải object) ⇒ model rỗng: placeholder sẽ render ra rỗng,
    /// đúng ý đồ kiểm tra template gọi sai tên biến.
    ///
    /// <para>So khớp khoá KHÔNG phân biệt hoa thường để người soạn gõ <c>{{TicketCode}}</c> hay
    /// <c>{{ticketCode}}</c> đều ra kết quả.</para>
    /// </summary>
    public static Dictionary<string, object?> Build(JsonElement? sampleData)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (sampleData is not { ValueKind: JsonValueKind.Object } element)
            return model;

        foreach (var property in element.EnumerateObject())
        {
            model[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                // Object/mảng lồng nhau: giữ nguyên dạng JSON thô. Model là phẳng nên không tra sâu
                // được, nhưng in ra vẫn hơn là nuốt mất giá trị.
                _ => property.Value.GetRawText(),
            };
        }

        return model;
    }
}
