using System.Security.Claims;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FileStorageService.Application.Authorization;

public class FileAuthorizationService : IFileAuthorizationService
{
    private const string AdminRole = "Admin";
    private const string ManagerRole = "Manager";
    private const string StaffRole = "Staff";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileAuthorizationService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentUserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var rawUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? user?.FindFirstValue("AccountId");

            return Guid.TryParse(rawUserId, out var userId) ? userId : null;
        }
    }

    public bool CanUpload(FilePurposeEnum purpose)
    {
        if (CurrentUserId is null)
            return false;

        return purpose switch
        {
            FilePurposeEnum.Firmware => HasRole(AdminRole),
            FilePurposeEnum.KbImage => HasAnyRole(AdminRole, ManagerRole),
            _ => true
        };
    }

    public bool CanRead(UploadedFile file)
    {
        if (CurrentUserId is null)
            return false;

        if (HasRole(AdminRole))
            return true;

        return file.Purpose switch
        {
            FilePurposeEnum.Avatar => true,
            FilePurposeEnum.KbImage => true,
            FilePurposeEnum.Firmware => false,
            // Ảnh ticket/maintenance: cho mọi user đã đăng nhập đọc. Customer (chủ ticket) phải xem
            // được ảnh do Staff/Manager đính kèm trong comment PUBLIC của ticket mình.
            // Kiểm soát "internal" nằm ở tầng comment (BE đã ẩn comment internal + fileId của nó
            // khỏi Customer), nên file layer không cần check CreatedBy ở đây.
            FilePurposeEnum.TicketAttachment or FilePurposeEnum.MaintenancePhoto => true,
            _ => file.CreatedBy == CurrentUserId
        };
    }

    public bool CanDelete(UploadedFile file)
    {
        if (CurrentUserId is null)
            return false;

        if (HasRole(AdminRole))
            return true;

        if (file.Purpose == FilePurposeEnum.Firmware)
            return false;

        return file.CreatedBy == CurrentUserId;
    }

    private bool HasAnyRole(params string[] roles)
    {
        return roles.Any(HasRole);
    }

    private bool HasRole(string role)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return false;

        return user.Claims.Any(claim =>
            (claim.Type == ClaimTypes.Role || claim.Type == "role")
            && string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
