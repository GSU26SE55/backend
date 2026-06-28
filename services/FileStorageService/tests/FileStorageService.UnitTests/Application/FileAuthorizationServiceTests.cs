using System.Security.Claims;
using FileStorageService.Application.Authorization;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FileStorageService.UnitTests.Application;

public class FileAuthorizationServiceTests
{
    private static readonly Guid CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static FileAuthorizationService BuildService(string role, Guid? userId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? CurrentUserId).ToString()),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);

        return new FileAuthorizationService(accessor.Object);
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
    public void CanRead_Customer_OtherUserFile_TicketRelatedPurpose_ReturnsTrue(FilePurposeEnum purpose)
    {
        var service = BuildService("Customer");
        var file = BuildFile(purpose, createdBy: OtherUserId);

        // TicketAttachment/MaintenancePhoto đọc được bởi mọi user đã đăng nhập (không giới hạn owner).
        service.CanRead(file).Should().BeTrue();
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
