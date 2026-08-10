using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Xunit;

namespace FileStorageService.IntegrationTests.ObjectStorage;

/// <summary>
/// GH-788 — bucket production để <c>anonymous set download</c>, tức ai biết object key là tải được
/// file mà không cần token nào.
///
/// <para>
/// Đính kèm ticket, ảnh bảo trì, tài liệu bảo hành nằm chung một bucket. Object key là GUID nên
/// "khó đoán" — nhưng key rò ra qua log truy cập, qua lịch sử trình duyệt, qua header Referer khi
/// người dùng mở link, và lúc đó không còn tầng nào chắn nữa. Toàn bộ phân quyền của
/// FileStorageService bị đi vòng.
/// </para>
/// <para>
/// Lớp test chạy MinIO thật và <c>mc</c> thật, đúng hai lệnh mà init container dùng, rồi bắn HTTP
/// trần vào object. Ba điều được chứng minh liền mạch:
/// <list type="number">
///   <item>chính sách CŨ (<c>anonymous set download</c>) thực sự cho tải không cần xác thực — không
///   dựng lại được lỗ hổng thì bản sửa chẳng chứng minh được gì;</item>
///   <item>chính sách MỚI (<c>anonymous set none</c>) trả 403 cho GET vô danh;</item>
///   <item>đường hợp lệ (presigned URL) vẫn tải được trên bucket private — siết mà làm chết đường
///   tải file thì chỉ là đổi lỗi này lấy lỗi khác.</item>
/// </list>
/// </para>
/// <para>
/// Mỗi test tự đặt chính sách ở dòng đầu nên chạy thứ tự nào cũng cho cùng kết quả, và mỗi test dùng
/// object key riêng để không giẫm lên nhau.
/// </para>
/// </summary>
public sealed class BucketPolicyTests : IClassFixture<MinioFixture>
{
    private const string Body = "noi dung dinh kem rieng tu";

    private readonly MinioFixture _minio;

    public BucketPolicyTests(MinioFixture minio) => _minio = minio;

    private Task PublicPolicyAsync() => _minio.McAsync($"mc anonymous set download local/{MinioFixture.Bucket}");
    private Task PrivatePolicyAsync() => _minio.McAsync($"mc anonymous set none local/{MinioFixture.Bucket}");

    /// <summary>
    /// Ký URL qua ĐÚNG service của production (<c>S3CompatibleFileStorageService</c>).
    /// </summary>
    /// <remarks>
    /// Không tự dựng <c>GetPreSignedUrlRequest</c> trong test: bug scheme (SDK mặc định HTTPS bất kể
    /// endpoint) nằm chính trong phương thức đó, nên viết lại nó ở đây là làm test xanh trong khi
    /// production vẫn phát ra link chết.
    /// </remarks>
    private Task<string> PresignAsync(string key, TimeSpan validFor)
        => _minio.NewStorageService().GetPresignedUrlAsync(key, validFor, CancellationToken.None);

    [Fact]
    public async Task OldPolicy_AnonymousDownload_ActuallyLeaksTheObject()
    {
        // Dựng lại đúng lỗ hổng trước khi chứng minh bản vá. Không có bước này thì test "403" phía
        // dưới có thể xanh vì lý do hoàn toàn khác (sai URL, sai bucket, object không tồn tại).
        const string key = "ticket-attachments/leak-probe.txt";
        await _minio.PutAsync(key, Body);

        var apply = await _minio.McAsync($"mc anonymous set download local/{MinioFixture.Bucket}");
        apply.ExitCode.Should().Be(0, apply.Stderr);

        var response = await _minio.AnonymousGetAsync(key);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "đây chính là lỗ hổng: biết object key là tải được, không cần token");
        (await response.Content.ReadAsStringAsync()).Should().Be(Body);
    }

    [Fact]
    public async Task NewPolicy_AnonymousGet_IsForbidden()
    {
        // Tiêu chí nghiệm thu: "Anonymous GET private object bị 403".
        const string key = "ticket-attachments/private-probe.txt";
        await _minio.PutAsync(key, Body);

        var apply = await _minio.McAsync($"mc anonymous set none local/{MinioFixture.Bucket}");
        apply.ExitCode.Should().Be(0, apply.Stderr);

        var response = await _minio.AnonymousGetAsync(key);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NewPolicy_RevokesAccessThatWasAlreadyGranted()
    {
        // Vì sao dùng `anonymous set none` chứ không chỉ xoá dòng lệnh đi: cụm production đã ở trạng
        // thái public rồi. Xoá lệnh thì lần deploy sau không đặt lại policy — nhưng policy cũ vẫn
        // nằm nguyên trong bucket. Phải có lệnh THU HỒI tường minh.
        const string key = "ticket-attachments/revoke-probe.txt";
        await _minio.PutAsync(key, Body);

        await PublicPolicyAsync();
        (await _minio.AnonymousGetAsync(key)).StatusCode.Should().Be(HttpStatusCode.OK, "tiền đề: đang public");

        await PrivatePolicyAsync();

        (await _minio.AnonymousGetAsync(key)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadedAttachment_IsUnreachableAnonymously_ButReachableViaPresignedUrl()
    {
        // Kịch bản đầu-cuối của tiêu chí nghiệm thu "private attachment chỉ tải qua luồng được
        // authorize": upload bằng chính service của FileStorageService, rồi thử cả hai đường.
        await PrivatePolicyAsync();
        var storage = _minio.NewStorageService();

        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Body));
        var uploaded = await storage.UploadAsync(
            content, "hoa-don-bao-hanh.txt", "text/plain", content.Length, "ticket-attachments");

        uploaded.PublicUrl.Should().BeNull(
            "PublicBaseUrl rỗng ⇒ API không phát URL công khai nữa; phát ra chỉ tạo link chết 403");

        (await _minio.AnonymousGetAsync(uploaded.ObjectKey)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "người ngoài biết object key vẫn không tải được");

        var response = await _minio.Http.GetAsync(
            await storage.GetPresignedUrlAsync(uploaded.ObjectKey, TimeSpan.FromMinutes(5), CancellationToken.None));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "presigned URL là đường tải được cấp phép");
        (await response.Content.ReadAsStringAsync()).Should().Be(Body);
    }

    [Fact]
    public async Task PresignedUrl_KeepsTheSchemeOfTheEndpoint()
    {
        // Bẫy đã trả giá: SDK mặc định ký scheme HTTPS bất kể ServiceURL/UseHttp. Endpoint HTTP (VPS
        // production, dev) nhận link https:// và chết ngay ở bắt tay TLS. Bucket public che mất lỗi
        // này suốt thời gian qua; đóng bucket lại là nó lộ ra ngay.
        var url = await PresignAsync("ticket-attachments/scheme-probe.txt", TimeSpan.FromMinutes(5));

        url.Should().StartWith("http://", "endpoint của fixture là HTTP — ký ra https là link chết");
    }

    [Fact]
    public async Task PrivateBucket_RejectsExpiredPresignedUrl()
    {
        // Chiều âm: nếu URL hết hạn vẫn tải được thì "có hạn" chỉ là trang trí, và một link rò ra sẽ
        // dùng được mãi mãi — không khác gì bucket public.
        const string key = "ticket-attachments/presign-expired.txt";
        await _minio.PutAsync(key, Body);
        await PrivatePolicyAsync();

        var response = await _minio.Http.GetAsync(await PresignAsync(key, TimeSpan.FromMinutes(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PrivateBucket_RejectsTamperedPresignedUrl()
    {
        // Đổi object key trên URL đã ký phải làm hỏng chữ ký. Nếu không, người có quyền xem file A
        // chỉ cần sửa link là lấy được file B của khách hàng khác.
        const string mine = "ticket-attachments/presign-mine.txt";
        const string theirs = "ticket-attachments/presign-cua-nguoi-khac.txt";
        await _minio.PutAsync(mine, Body);
        await _minio.PutAsync(theirs, "khong duoc phep doc");
        await PrivatePolicyAsync();

        var tampered = (await PresignAsync(mine, TimeSpan.FromMinutes(5)))
            .Replace("presign-mine.txt", "presign-cua-nguoi-khac.txt", StringComparison.Ordinal);

        var response = await _minio.Http.GetAsync(tampered);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PrivateBucket_RejectsDefaultCredentials()
    {
        // Nối với ObjectStorageCredentialGuard: sau khi root user là giá trị sinh ngẫu nhiên,
        // minioadmin/minioadmin không còn mở được gì — đó mới là điều khiến việc bỏ mặc định có ý nghĩa.
        const string key = "ticket-attachments/creds-probe.txt";
        await _minio.PutAsync(key, Body);
        await PrivatePolicyAsync();

        using var wrong = _minio.NewClient("minioadmin", "minioadmin");

        Func<Task> act = async () => await wrong.GetObjectAsync(MinioFixture.Bucket, key);

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }
}
