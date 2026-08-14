using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace FileStorageService.Infrastructure.Options;

/// <summary>
/// Dựng client S3 từ <see cref="ObjectStorageOptions"/>.
/// </summary>
/// <remarks>
/// Tách khỏi phần đăng ký DI để kiểm thử được: riêng cờ <see cref="AmazonS3Config.UseHttp"/> quyết
/// định presigned URL dùng scheme nào, mà sai scheme thì lỗi chỉ hiện ra ở trình duyệt người dùng
/// chứ không ở phía server (xem <see cref="BuildConfig"/>).
/// </remarks>
public static class ObjectStorageClientFactory
{
    /// <summary>
    /// Địa chỉ dùng để gọi MinIO/S3: nội bộ (container → container) hay công khai (browser tải
    /// presigned URL). Không cấu hình <c>PublicServiceUrl</c> thì rơi về địa chỉ nội bộ.
    /// </summary>
    public static string ResolveServiceUrl(ObjectStorageOptions options, bool useInternal)
    {
        ArgumentNullException.ThrowIfNull(options);

        return useInternal || string.IsNullOrWhiteSpace(options.PublicServiceUrl)
            ? options.ServiceUrl
            : options.PublicServiceUrl;
    }

    /// <summary>
    /// GH-788 — <c>UseHttp</c> phải khớp scheme của endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AWS SDK mặc định <c>UseHttp = false</c> và <b>bỏ qua scheme trong <c>ServiceURL</c></b> khi ký
    /// presigned URL: đặt <c>ServiceURL = http://localhost:9090</c> vẫn sinh ra
    /// <c>https://localhost:9090/...</c>. Đo trực tiếp trên MinIO thật thì client báo
    /// <i>"The SSL connection could not be established"</i>.
    /// </para>
    /// <para>
    /// Điều này quan trọng hơn hẳn từ khi bucket thành private: presigned URL là đường tải file DUY
    /// NHẤT còn lại. Trước đây bucket public che mất lỗi — người dùng tải bằng URL thường nên không
    /// ai phát hiện link presigned vốn đã hỏng.
    /// </para>
    /// </remarks>
    public static AmazonS3Config BuildConfig(ObjectStorageOptions options, bool useInternal)
    {
        var serviceUrl = ResolveServiceUrl(options, useInternal);

        return new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,
            UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// GH-788 — scheme cho presigned URL.
    /// </summary>
    /// <remarks>
    /// <see cref="GetPreSignedUrlRequest.Protocol"/> mặc định là <see cref="Protocol.HTTPS"/> và
    /// <b>không nhìn tới <c>ServiceURL</c> lẫn <c>UseHttp</c></b>: đo trực tiếp trên MinIO thật, cấu
    /// hình <c>ServiceURL=http://…</c>, <c>UseHttp=true</c>, <c>DetermineServiceURL()=http://…</c>
    /// vẫn sinh ra link <c>https://…</c> và client chết ở bước bắt tay TLS.
    /// <para>
    /// Trước GH-788 lỗi này vô hình vì bucket là public — người dùng tải bằng URL thường, không ai
    /// đi qua đường presigned. Đóng bucket lại là presigned trở thành đường DUY NHẤT, nên phải sửa
    /// cùng lúc, nếu không "vá bảo mật" sẽ thành "hỏng tải file".
    /// </para>
    /// </remarks>
    public static Protocol ResolveProtocol(ObjectStorageOptions options)
        => ResolveServiceUrl(options, useInternal: false)
            .StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;

    public static IAmazonS3 Create(ObjectStorageOptions options, bool useInternal)
        => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            BuildConfig(options, useInternal));
}
