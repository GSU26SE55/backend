namespace FileStorageService.Infrastructure.Options;

/// <summary>
/// GH-788 — chặn việc khởi động FileStorageService bằng credential object storage yếu hoặc mặc định.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ObjectStorageOptions"/> trước đây đặt sẵn <c>minioadmin/minioadmin</c> làm giá trị mặc
/// định ngay trong mã. Nghĩa là dù triển khai quên cấu hình hoàn toàn, service vẫn khởi động "thành
/// công" và kết nối bằng credential ai cũng đoán được — không lỗi, không cảnh báo, không dấu vết
/// nào trong log. Vá ở tầng compose/Helm thôi thì chưa đủ: chỉ cần một đường triển khai khác (chạy
/// tay, môi trường mới, test dựng cụm) là mặc định lại sống dậy.
/// </para>
/// <para>
/// Kiểm ở đây là <b>thuần hàm</b> để kiểm thử được, và trả về TOÀN BỘ lỗi thay vì dừng ở lỗi đầu —
/// người triển khai sửa một lượt, không phải chạy lại 3 lần để lộ dần từng lỗi.
/// </para>
/// <para>
/// Môi trường CỤC BỘ được nới có chủ ý: <c>docker-compose.yml</c> và <c>.env</c> dùng
/// <c>minioadmin</c> cho máy cá nhân, siết ở đó chỉ khiến mọi người tắt kiểm tra đi.
/// </para>
/// <para>
/// <b>Cục bộ nghĩa là <c>Development</c> HOẶC <c>Docker</c></b>, không phải chỉ
/// <c>Development</c>. Bản đầu tiên của phép kiểm này chỉ nới cho <c>Development</c>, mà stack
/// docker-compose của repo lại đặt <c>ASPNETCORE_ENVIRONMENT=Docker</c> — đo được lúc chạy thật:
/// <c>filestorageservice</c> vào crash-loop (exit 133) và không đường triển khai cục bộ nào lên
/// được. Cả 8 service khác trong repo đã coi <c>"Docker"</c> là môi trường cục bộ từ trước
/// (chúng tắt HTTPS redirection theo <c>IsEnvironment("Docker")</c>), nên đây là quy ước sẵn có
/// chứ không phải ngoại lệ tôi đặt thêm.
/// </para>
/// </remarks>
public static class ObjectStorageCredentialGuard
{
    /// <summary>
    /// Tên các môi trường được coi là CỤC BỘ — nơi credential mặc định được chấp nhận.
    /// </summary>
    /// <remarks>
    /// Giữ ở một chỗ để phép kiểm và mọi call-site không thể lệch nhau. Thêm môi trường cục bộ mới
    /// thì thêm vào đây, đừng rải điều kiện ra từng nơi.
    /// </remarks>
    public static readonly IReadOnlyList<string> LocalEnvironmentNames = ["Development", "Docker"];

    /// <summary>
    /// Tên môi trường này có phải môi trường cục bộ không (không phân biệt hoa thường).
    /// </summary>
    public static bool IsLocalEnvironment(string? environmentName)
        => !string.IsNullOrWhiteSpace(environmentName)
           && LocalEnvironmentNames.Any(
               name => string.Equals(name, environmentName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Những giá trị KHÔNG được dùng ngoài môi trường cục bộ — mặc định của image, giá trị mẫu
    /// trong tài liệu, và các chỗ giữ chỗ mà người ta hay quên thay.
    /// </summary>
    /// <remarks>
    /// So sánh không phân biệt hoa thường: <c>MinioAdmin</c> cũng dễ đoán y như <c>minioadmin</c>.
    /// </remarks>
    public static readonly IReadOnlyList<string> ForbiddenValues =
    [
        "minioadmin",
        "admin",
        "password",
        "changeme",
        "change_me",
        "CHANGE_ME",
        "secret",
        "THAY-BANG-GIA-TRI-SINH-NGAU-NHIEN",
    ];

    /// <summary>
    /// Độ dài tối thiểu của secret key ngoài môi trường cục bộ.
    /// </summary>
    /// <remarks>
    /// 16 ký tự là ngưỡng để một secret sinh ngẫu nhiên không bị dò cạn trong thực tế; đặt cao hơn
    /// nữa sẽ loại oan những secret hợp lệ đang dùng. Access key không áp ngưỡng này vì nó là định
    /// danh, không phải bí mật — nhưng vẫn không được là giá trị mặc định.
    /// </remarks>
    public const int MinimumSecretKeyLength = 16;

    /// <summary>
    /// Trả về danh sách lỗi cấu hình. Rỗng nghĩa là hợp lệ.
    /// </summary>
    /// <param name="options">Cấu hình đã bind từ section <c>ObjectStorage</c>.</param>
    /// <param name="isLocalEnvironment">
    /// Đang chạy ở môi trường CỤC BỘ hay không — xem <see cref="IsLocalEnvironment(string)"/>.
    /// Đặt tên theo đúng nghĩa thay vì <c>isDevelopment</c>: cái tên cũ chính là thứ khiến stack
    /// docker (môi trường <c>Docker</c>) bị chặn oan.
    /// </param>
    public static IReadOnlyList<string> Validate(ObjectStorageOptions options, bool isLocalEnvironment)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        // Thiếu credential là lỗi ở MỌI môi trường: không có mặc định để rơi về nữa.
        if (string.IsNullOrWhiteSpace(options.AccessKey))
            errors.Add("ObjectStorage__AccessKey is not configured.");

        if (string.IsNullOrWhiteSpace(options.SecretKey))
            errors.Add("ObjectStorage__SecretKey is not configured.");

        if (isLocalEnvironment)
            return errors;

        if (IsForbidden(options.AccessKey))
            errors.Add(
                $"ObjectStorage__AccessKey is using a default/guessable value ('{options.AccessKey}'). "
                + "Generate a new one: openssl rand -hex 16");

        if (IsForbidden(options.SecretKey))
            errors.Add(
                "ObjectStorage__SecretKey is using a default/guessable value. "
                + "Generate a new one: openssl rand -base64 32");
        else if (!string.IsNullOrWhiteSpace(options.SecretKey)
                 && options.SecretKey.Length < MinimumSecretKeyLength)
            errors.Add(
                $"ObjectStorage__SecretKey is too short ({options.SecretKey.Length} characters, "
                + $"minimum {MinimumSecretKeyLength}).");

        return errors;
    }

    private static bool IsForbidden(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && ForbiddenValues.Any(f => string.Equals(f, value.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ném <see cref="InvalidOperationException"/> kèm toàn bộ lỗi nếu cấu hình không hợp lệ.
    /// </summary>
    public static void ThrowIfInvalid(ObjectStorageOptions options, bool isLocalEnvironment)
    {
        var errors = Validate(options, isLocalEnvironment);
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(
            "Invalid ObjectStorage configuration — FileStorageService refuses to start (GH-788):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
    }
}
