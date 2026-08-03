namespace NotificationService.Application.Templates;

/// <summary>
/// Kiểm cú pháp Handlebars của một template TRƯỚC khi lưu.
///
/// <para>Vì sao không nằm trong <c>ValidateAsync()</c> của command: <c>IValidatable</c> là hàm thuần
/// trên chính command, không nhận được <see cref="ITemplateRenderer"/> qua DI. Nên phần kiểm hình
/// thức (rỗng, quá dài) ở <c>ValidateAsync</c>, còn phần kiểm ngữ nghĩa này ở handler.</para>
///
/// <para>Vì sao phải kiểm: template hỏng cú pháp KHÔNG chặn việc gửi — dispatcher bắt exception rồi
/// lặng lẽ rơi về chuỗi hardcode trong consumer. Nghĩa là lưu một template hỏng thì mọi thông báo
/// của cặp đó mất nội dung tuỳ biến mà không ai hay. Chặn ngay lúc lưu là chỗ duy nhất còn kịp báo
/// cho người soạn.</para>
/// </summary>
public static class TemplateSyntaxGuard
{
    /// <summary>
    /// Trả về thông báo lỗi nếu tiêu đề hoặc thân không compile được; <c>null</c> nếu cả hai đều hợp lệ.
    /// </summary>
    public static string? FindSyntaxError(
        ITemplateRenderer renderer,
        string titleTemplate,
        string bodyTemplate)
    {
        // Model rỗng là đủ để phát hiện lỗi CÚ PHÁP: Handlebars compile trước rồi mới tra biến,
        // biến thiếu chỉ render ra rỗng chứ không ném.
        var emptyModel = new Dictionary<string, object?>();

        foreach (var (field, text) in new[]
                 {
                     ("TitleTemplate", titleTemplate),
                     ("BodyTemplate", bodyTemplate),
                 })
        {
            try
            {
                renderer.RenderInline(text, emptyModel);
            }
            catch (Exception ex)
            {
                return $"{field} hỏng cú pháp Handlebars: {ex.Message}";
            }
        }

        return null;
    }
}
