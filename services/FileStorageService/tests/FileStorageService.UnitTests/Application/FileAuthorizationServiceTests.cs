using System.Security.Claims;
using FileStorageService.Application.Authorization;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SharedKernels.Security;

namespace FileStorageService.UnitTests.Application;

public class FileAuthorizationServiceTests
{
    private static readonly Guid CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Khoá ký grant dùng trong test (GH-723).</summary>
    private const string TestSecretKey = "test-secret-key-for-file-access-grant-0123456789";

    private static FileAuthorizationService BuildService(string role, Guid? userId = null, string? grant = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? CurrentUserId).ToString()),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        if (grant is not null)
        {
            httpContext.Request.QueryString =
                new QueryString($"?{FileAccessGrant.QueryParameterName}={Uri.EscapeDataString(grant)}");
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SecretKey"] = TestSecretKey })
            .Build();

        return new FileAuthorizationService(accessor.Object, configuration);
    }

    private static UploadedFile BuildFile(FilePurposeEnum purpose, Guid createdBy)
        => new()
        {
            Id = Guid.NewGuid(),
            ObjectKey = "obj/key",
            OriginalFileName = "file.png",
            ContentType = "image/png",
            Size = 100,
            FolderName = "folder",
            Purpose = purpose,
            Status = FileStatusEnum.Uploaded,
            CreatedBy = createdBy
        };

    // ── CanUpload ──

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Staff")]
    public void CanUpload_KbImage_InternalRoles_ReturnsTrue(string role)
    {
        // Staff là người soạn bài Knowledge Base chính nên phải chèn được ảnh vào bài.
        var service = BuildService(role);

        service.CanUpload(FilePurposeEnum.KbImage).Should().BeTrue();
    }

    [Fact]
    public void CanUpload_KbImage_Customer_ReturnsFalse()
    {
        var service = BuildService("Customer");

        service.CanUpload(FilePurposeEnum.KbImage).Should().BeFalse();
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Staff")]
    [InlineData("Customer")]
    public void CanUpload_Firmware_NonAdmin_ReturnsFalse(string role)
    {
        var service = BuildService(role);

        service.CanUpload(FilePurposeEnum.Firmware).Should().BeFalse();
    }

    [Fact]
    public void CanUpload_Firmware_Admin_ReturnsTrue()
    {
        var service = BuildService("Admin");

        service.CanUpload(FilePurposeEnum.Firmware).Should().BeTrue();
    }

    [Theory]
    [InlineData(FilePurposeEnum.TicketAttachment)]
    [InlineData(FilePurposeEnum.MaintenancePhoto)]
    public void CanRead_Manager_OtherUserFile_TicketRelatedPurpose_ReturnsTrue(FilePurposeEnum purpose)
    {
        var service = BuildService("Manager");
        var file = BuildFile(purpose, createdBy: OtherUserId);

        service.CanRead(file).Should().BeTrue();
    }

    [Theory]
    [InlineData(FilePurposeEnum.TicketAttachment)]
    [InlineData(FilePurposeEnum.MaintenancePhoto)]
    public void CanRead_Staff_OtherUserFile_TicketRelatedPurpose_ReturnsTrue(FilePurposeEnum purpose)
    {
        var service = BuildService("Staff");
        var file = BuildFile(purpose, createdBy: OtherUserId);

        service.CanRead(file).Should().BeTrue();
    }

    [Theory]
    [InlineData(FilePurposeEnum.TicketAttachment)]
    [InlineData(FilePurposeEnum.MaintenancePhoto)]
    public void CanRead_Customer_OtherUserFile_TicketRelatedPurpose_ReturnsFalse(FilePurposeEnum purpose)
    {
        // GH-723 — test này TRƯỚC ĐÂY khẳng định `ReturnsTrue` với ghi chú "đọc được bởi mọi
        // user đã đăng nhập". Đó chính là lỗ hổng: biết fileId là tải được file của ticket
        // người khác. Issue #723 đổi spec thành "403 trừ uploader, ticket participant,
        // assigned staff hoặc Manager/Admin", nên kỳ vọng bị đảo lại có chủ đích.
        // Customer là participant hợp lệ thì đi qua grant — xem các test grant bên dưới.
        var service = BuildService("Customer");
        var file = BuildFile(purpose, createdBy: OtherUserId);

        service.CanRead(file).Should().BeFalse();
    }

    // ── GH-723: grant do TicketService cấp ──

    [Fact]
    public void CanRead_Customer_OtherUserTicketAttachment_WithValidGrant_ReturnsTrue()
    {
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);
        var grant = FileAccessGrant.Issue(
            TestSecretKey, file.Id, CurrentUserId, DateTimeOffset.UtcNow.AddMinutes(5));

        BuildService("Customer", grant: grant).CanRead(file).Should().BeTrue();
    }

    [Fact]
    public void CanRead_Customer_GrantIssuedForAnotherUser_ReturnsFalse()
    {
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);
        // Grant cấp cho người khác — chuyển tay không được dùng lại.
        var grant = FileAccessGrant.Issue(
            TestSecretKey, file.Id, OtherUserId, DateTimeOffset.UtcNow.AddMinutes(5));

        BuildService("Customer", grant: grant).CanRead(file).Should().BeFalse();
    }

    [Fact]
    public void CanRead_Customer_GrantIssuedForAnotherFile_ReturnsFalse()
    {
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);
        var grant = FileAccessGrant.Issue(
            TestSecretKey, Guid.NewGuid(), CurrentUserId, DateTimeOffset.UtcNow.AddMinutes(5));

        BuildService("Customer", grant: grant).CanRead(file).Should().BeFalse();
    }

    [Fact]
    public void CanRead_Customer_ExpiredGrant_ReturnsFalse()
    {
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);
        var grant = FileAccessGrant.Issue(
            TestSecretKey, file.Id, CurrentUserId, DateTimeOffset.UtcNow.AddSeconds(-1));

        BuildService("Customer", grant: grant).CanRead(file).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("rác")]
    [InlineData("9999999999.chữ-ký-bịa")]
    public void CanRead_Customer_TamperedGrant_ReturnsFalse(string grant)
    {
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);

        BuildService("Customer", grant: grant).CanRead(file).Should().BeFalse();
    }

    [Fact]
    public void CanRead_Customer_MaintenancePhoto_GrantDoesNotApply_ReturnsFalse()
    {
        // Maintenance log là tài liệu nội bộ (controller chỉ mở cho Staff/Manager/Admin)
        // ⇒ grant KHÔNG mở đường cho Customer.
        var file = BuildFile(FilePurposeEnum.MaintenancePhoto, createdBy: OtherUserId);
        var grant = FileAccessGrant.Issue(
            TestSecretKey, file.Id, CurrentUserId, DateTimeOffset.UtcNow.AddMinutes(5));

        BuildService("Customer", grant: grant).CanRead(file).Should().BeFalse();
    }

    [Theory]
    [InlineData(FilePurposeEnum.TicketAttachment)]
    [InlineData(FilePurposeEnum.MaintenancePhoto)]
    public void CanRead_Customer_OwnFile_TicketRelatedPurpose_ReturnsTrue(FilePurposeEnum purpose)
    {
        var service = BuildService("Customer");
        var file = BuildFile(purpose, createdBy: CurrentUserId);

        service.CanRead(file).Should().BeTrue();
    }

    [Fact]
    public void CanRead_Manager_FirmwareFile_ReturnsFalse()
    {
        var service = BuildService("Manager");
        var file = BuildFile(FilePurposeEnum.Firmware, createdBy: OtherUserId);

        service.CanRead(file).Should().BeFalse();
    }

    [Fact]
    public void CanRead_Staff_FirmwareFile_ReturnsFalse()
    {
        var service = BuildService("Staff");
        var file = BuildFile(FilePurposeEnum.Firmware, createdBy: OtherUserId);

        service.CanRead(file).Should().BeFalse();
    }

    [Fact]
    public void CanRead_Admin_FirmwareFile_ReturnsTrue()
    {
        var service = BuildService("Admin");
        var file = BuildFile(FilePurposeEnum.Firmware, createdBy: OtherUserId);

        service.CanRead(file).Should().BeTrue();
    }

    [Fact]
    public void CanRead_AnyRole_AvatarFile_ReturnsTrue()
    {
        var service = BuildService("Customer");
        var file = BuildFile(FilePurposeEnum.Avatar, createdBy: OtherUserId);

        service.CanRead(file).Should().BeTrue();
    }

    [Fact]
    public void CanDelete_Manager_OtherUserTicketAttachment_ReturnsFalse()
    {
        var service = BuildService("Manager");
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);

        service.CanDelete(file).Should().BeFalse();
    }

    [Fact]
    public void CanDelete_Staff_OtherUserTicketAttachment_ReturnsFalse()
    {
        var service = BuildService("Staff");
        var file = BuildFile(FilePurposeEnum.TicketAttachment, createdBy: OtherUserId);

        service.CanDelete(file).Should().BeFalse();
    }
}
