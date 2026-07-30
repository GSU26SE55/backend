using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Sprint 6.3 NOTI3-15 (#715) — hủy đăng ký một chạm từ email (RFC 8058).
///
/// **Vì sao public:** Gmail/Yahoo gửi <c>POST</c> tự động khi người dùng bấm nút "Hủy đăng ký" ngay
/// trong giao diện hộp thư — không kèm cookie hay JWT. Vì vậy endpoint xác thực bằng **token ký
/// HMAC** trong URL thay vì bằng đăng nhập; token ràng đúng một người dùng và một nhóm, có hạn dùng.
///
/// **Vì sao phải có:** từ 2024 Gmail và Yahoo bắt buộc người gửi số lượng lớn hỗ trợ hủy một chạm.
/// Không có nút hủy, người nhận sẽ bấm "báo cáo spam" — tỷ lệ spam vượt 0.3% là mất reputation của
/// domain <c>solarbattery.site</c> đang trong giai đoạn warm-up.
/// </summary>
[ApiController]
[Route("api/notification-unsubscribe")]
[AllowAnonymous]
public class NotificationUnsubscribeController : ControllerBase
{
    private readonly UnsubscribeTokenService _tokens;
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<NotificationUnsubscribeController> _logger;

    public NotificationUnsubscribeController(
        UnsubscribeTokenService tokens,
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<NotificationUnsubscribeController> logger)
    {
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Hủy đăng ký một chạm — điểm mà Gmail/Yahoo gọi tự động.
    /// </summary>
    /// <remarks>
    /// **Quyền:** công khai, xác thực bằng token ký HMAC trong query.
    ///
    /// Tắt kênh **Email** cho đúng NHÓM ghi trong token, không tắt toàn bộ email: người dùng hủy vì
    /// bị chat làm phiền vẫn phải nhận được cảnh báo SLA.
    ///
    /// Idempotent — email client có thể gửi lại; token sai/hết hạn trả **400**.
    /// </remarks>
    /// <response code="200">Đã tắt email cho nhóm tương ứng (hoặc vốn đã tắt).</response>
    /// <response code="400">Token thiếu, sai chữ ký, hoặc hết hạn.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OneClickUnsubscribe(
        [FromQuery] string? token, CancellationToken cancellationToken)
    {
        if (!_tokens.TryValidate(token, out var userId, out var category))
        {
            // Không nói rõ sai chữ ký hay hết hạn — tránh giúp người dò thu hẹp phạm vi.
            _logger.LogWarning("Unsubscribe: token không hợp lệ (IP {Ip}).", HttpContext.Connection.RemoteIpAddress);
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = "Liên kết hủy đăng ký không hợp lệ hoặc đã hết hạn.",
            });
        }

        var entity = await _unitOfWork.NotificationCategoryPreferences.GetAllAsync()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category && !p.IsDeleted, cancellationToken);

        if (entity is null)
        {
            // Chưa có dòng tuỳ chọn cho nhóm này ⇒ tạo mới, chỉ tắt Email.
            // Các kênh khác giữ mặc định: người dùng hủy EMAIL, không phải hủy mọi kênh.
            await _unitOfWork.NotificationCategoryPreferences.AddAsync(new NotificationCategoryPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Category = category,
                EmailEnabled = false,
                PushEnabled = true,
                SmsEnabled = false,
                InAppEnabled = true,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            entity.EmailEnabled = false;
            entity.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.NotificationCategoryPreferences.UpdateAsync(entity);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Dispatcher cache tuỳ chọn nhóm 5 phút — không xoá thì người dùng còn nhận email tới 5 phút
        // sau khi bấm hủy, và họ sẽ báo cáo spam.
        await _cache.RemoveAsync($"notif_cat_pref:{userId}:{(int)category}", cancellationToken);

        _logger.LogInformation("Unsubscribe: user {UserId} đã tắt email cho nhóm {Category}.", userId, category);

        return Ok(new CommonResponse<object>
        {
            IsSuccess = true,
            Message = $"Đã ngừng gửi email nhóm '{category}'. Các kênh khác không thay đổi.",
        });
    }

    /// <summary>
    /// Trang xác nhận khi người dùng bấm thẳng vào liên kết trong nội dung email.
    /// </summary>
    /// <remarks>
    /// **Quyền:** công khai, cùng token với <c>POST</c>.
    ///
    /// Chỉ **hiển thị**, KHÔNG thay đổi gì — <c>GET</c> phải an toàn (RFC 9110): trình quét link của
    /// hộp thư tự mở mọi URL trong email, nếu <c>GET</c> cũng hủy thì người dùng bị hủy oan mà
    /// không hề bấm.
    /// </remarks>
    /// <response code="200">Token hợp lệ — trả về thông tin nhóm sẽ bị hủy.</response>
    /// <response code="400">Token thiếu, sai chữ ký, hoặc hết hạn.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Preview([FromQuery] string? token)
    {
        if (!_tokens.TryValidate(token, out _, out var category))
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = "Liên kết hủy đăng ký không hợp lệ hoặc đã hết hạn.",
            });
        }

        return Ok(new CommonResponse<object>
        {
            IsSuccess = true,
            Message = "Xác nhận để ngừng nhận email nhóm này.",
            Data = new { category = category.ToString(), confirmMethod = "POST" },
        });
    }
}
