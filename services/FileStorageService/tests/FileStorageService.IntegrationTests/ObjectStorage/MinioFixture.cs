using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using FileStorageService.Application.Interfaces;
using FileStorageService.Infrastructure.Options;
using FileStorageService.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace FileStorageService.IntegrationTests.ObjectStorage;

/// <summary>
/// GH-788 — MinIO thật + <c>mc</c> thật, dùng chung cho cả lớp test.
/// </summary>
/// <remarks>
/// <para>
/// Dùng chung thay vì dựng lại cho từng test vì mỗi lượt tốn hai container; mà chính sách bucket là
/// thứ <b>mỗi test tự đặt ở dòng đầu</b>, nên trạng thái sót lại không ảnh hưởng — thứ tự chạy thế
/// nào cũng cho cùng kết quả.
/// </para>
/// <para>
/// <c>mc</c> phải nằm ở container riêng: image <c>minio/minio</c> không có nó. Hai container nối qua
/// một network với alias <c>minio</c>, giống hệt job init trong compose và Helm — nhờ vậy test chạy
/// đúng lệnh được ship chứ không phải một bản dựng lại bằng SDK.
/// </para>
/// </remarks>
public sealed class MinioFixture : IAsyncLifetime
{
    private const string DefaultMinioImage = "minio/minio:RELEASE.2025-04-22T22-12-26Z";
    private const string DefaultMcImage = "minio/mc:RELEASE.2025-04-16T18-13-26Z";

    public const string RootUser = "gh788-root-user";
    public const string RootPassword = "gh788-root-password-du-dai";
    public const string Bucket = "solar-battery-files";

    private INetwork _network = null!;
    private IContainer _minio = null!;
    private IContainer _mc = null!;

    public IAmazonS3 S3 { get; private set; } = null!;
    public HttpClient Http { get; } = new();
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var minioImage = Environment.GetEnvironmentVariable("FILESTORAGE_TEST_MINIO_IMAGE")
            ?? DefaultMinioImage;
        var mcImage = Environment.GetEnvironmentVariable("FILESTORAGE_TEST_MC_IMAGE")
            ?? DefaultMcImage;

        _network = new NetworkBuilder().Build();

        _minio = new ContainerBuilder()
            .WithImage(minioImage)
            .WithNetwork(_network)
            .WithNetworkAliases("minio")
            .WithEnvironment("MINIO_ROOT_USER", RootUser)
            .WithEnvironment("MINIO_ROOT_PASSWORD", RootPassword)
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/minio/health/live").ForPort(9000)))
            .Build();

        await _minio.StartAsync();

        BaseUrl = $"http://{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}";

        _mc = new ContainerBuilder()
            .WithImage(mcImage)
            .WithNetwork(_network)
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand("sleep 900")
            .Build();

        await _mc.StartAsync();

        var alias = await McAsync($"mc alias set local http://minio:9000 {RootUser} {RootPassword}");
        alias.ExitCode.Should().Be(0, alias.Stderr);

        var makeBucket = await McAsync($"mc mb -p local/{Bucket}");
        makeBucket.ExitCode.Should().Be(0, makeBucket.Stderr);

        S3 = NewClient(RootUser, RootPassword);
    }

    public async Task DisposeAsync()
    {
        Http.Dispose();
        S3?.Dispose();
        if (_mc is not null)
            await _mc.DisposeAsync();
        if (_minio is not null)
            await _minio.DisposeAsync();
        if (_network is not null)
            await _network.DeleteAsync();
    }

    /// <summary>
    /// Dựng client qua ĐÚNG factory mà production dùng.
    /// </summary>
    /// <remarks>
    /// Cố ý không tự <c>new AmazonS3Client(...)</c>: làm vậy là test một bản dựng lại của mình, và
    /// bug thật (<c>UseHttp</c> sai ⇒ presigned URL ra scheme <c>https</c> trên endpoint HTTP) sẽ
    /// không bao giờ lọt vào tầm nhìn của test.
    /// </remarks>
    public IAmazonS3 NewClient(string accessKey, string secretKey)
        => ObjectStorageClientFactory.Create(
            new ObjectStorageOptions
            {
                ServiceUrl = BaseUrl,
                AccessKey = accessKey,
                SecretKey = secretKey,
                Region = "us-east-1",
                ForcePathStyle = true,
            },
            useInternal: true);

    /// <summary>Chạy một lệnh <c>mc</c> thật trong container — cùng binary mà job init dùng.</summary>
    public Task<ExecResult> McAsync(string command) => _mc.ExecAsync(["/bin/sh", "-c", command]);

    /// <summary>Cấu hình trỏ vào MinIO của fixture, dùng để dựng service thật.</summary>
    public ObjectStorageOptions BuildOptions() => new()
    {
        ServiceUrl = BaseUrl,
        PublicServiceUrl = BaseUrl,
        BucketName = Bucket,
        AccessKey = RootUser,
        SecretKey = RootPassword,
        Region = "us-east-1",
        ForcePathStyle = true,
        CreateBucketIfNotExists = false,
        PublicBaseUrl = null,
    };

    /// <summary>
    /// Dựng <see cref="S3CompatibleFileStorageService"/> THẬT — cùng lớp mà handler upload/presign
    /// của FileStorageService gọi.
    /// </summary>
    /// <remarks>
    /// Test đi qua lớp này thay vì tự gọi SDK, vì bug <c>Protocol</c> mặc định HTTPS nằm CHÍNH trong
    /// phương thức <c>GetPresignedUrlAsync</c>. Tự dựng request trong test là tự viết lại đoạn đang
    /// hỏng, và test sẽ xanh trong khi production vẫn trả link chết.
    /// </remarks>
    public IObjectStorageService NewStorageService()
    {
        var options = BuildOptions();
        return new S3CompatibleFileStorageService(
            ObjectStorageClientFactory.Create(options, useInternal: true),
            ObjectStorageClientFactory.Create(options, useInternal: false),
            Microsoft.Extensions.Options.Options.Create(options));
    }

    public Task PutAsync(string key, string body) => S3.PutObjectAsync(new PutObjectRequest
    {
        BucketName = Bucket,
        Key = key,
        ContentBody = body,
        ContentType = "text/plain",
    });

    /// <summary>GET không kèm chữ ký, không kèm token — đúng thứ một người ngoài làm được.</summary>
    public Task<HttpResponseMessage> AnonymousGetAsync(string key)
        => Http.GetAsync($"{BaseUrl}/{Bucket}/{key}");
}
