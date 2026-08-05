using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Application.Common.Options;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.UnitTests.Handlers.Auth;

/// <summary>
/// GH-776 — endpoint OAuth Token Introspection mở cho bất kỳ ai và không giới hạn tần suất.
///
/// <para>
/// RFC 7662 §2.1 nói thẳng: endpoint introspection PHẢI yêu cầu một dạng ủy quyền. Bản cũ không có
/// <c>[Authorize]</c>, không API key, không rate-limit. Đo được lúc chạy thật: 12 request liên tiếp
/// KHÔNG kèm Authorization đều trả 200 với <c>active=true</c> — tức bất kỳ ai cũng biết được một
/// token còn sống hay đã bị thu hồi, và mỗi lần hỏi đều tốn một lần kiểm chữ ký JWT cộng một truy
/// vấn Redis.
/// </para>
/// <para>
/// Đã tìm toàn repo: hiện KHÔNG service nào gọi endpoint này. Nên fail-closed khi thiếu cấu hình
/// không làm hỏng luồng nào đang chạy.
/// </para>
/// </summary>
public class IntrospectTokenAuthorizationTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";   // đúng 32 ký tự

    private readonly Mock<IJwtHelper> _jwt = new();
    private readonly Mock<ITokenRevocationStore> _revocation = new();

    public IntrospectTokenAuthorizationTests()
    {
        _jwt.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns((true, (string?)null));
        _revocation.Setup(r => r.IsRevokedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private IntrospectTokenCommandHandler Handler(string? configuredKey = ValidKey)
        => new(_jwt.Object, _revocation.Object,
            Options.Create(new IntrospectionOptions { ApiKey = configuredKey }));

    /// <summary>JWT thật (ký HMAC) để handler đọc được claim sau khi qua bước xác thực khoá.</summary>
    private static string RealToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-at-least-32-bytes-long!!"));
        var token = new JwtSecurityToken(
            claims: new[]
            {
                // Handler đọc claim "AccountId" (không phải "sub") — đúng như token AuthService cấp
                // thật. Dựng token thiếu claim này là test một hình dạng không tồn tại.
                new Claim("AccountId", Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task AnonymousCall_WithoutKey_Is401()
    {
        // Đây chính là ca đo được lúc chạy thật: không kèm gì cả mà vẫn nhận 200 active=true.
        var resp = await Handler().Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = null },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(401);
        resp.Data.Should().BeNull("không được rò bất kỳ thông tin nào về token cho người gọi trái phép");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sai-khoa")]
    [InlineData("0123456789abcdef0123456789abcdeF")]   // lệch đúng một ký tự cuối
    public async Task WrongKey_Is401(string presented)
    {
        var resp = await Handler().Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = presented },
            CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        resp.Data.Should().BeNull();
    }

    [Fact]
    public async Task ValidKey_StillIntrospectsSuccessfully()
    {
        // Chống hồi quy: lớp bảo vệ không được làm hỏng chính chức năng của endpoint.
        var resp = await Handler().Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = ValidKey },
            CancellationToken.None);

        resp.StatusCode.Should().Be(200);
        resp.Data!.Active.Should().BeTrue();
        resp.Data.Sub.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidKey_WithSurroundingWhitespace_IsAccepted()
    {
        // Header đi qua nhiều tầng proxy dễ dính khoảng trắng; chặn vì lý do đó là gây sự cố vận
        // hành mà không thêm chút an toàn nào.
        var resp = await Handler().Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = $"  {ValidKey}  " },
            CancellationToken.None);

        resp.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qua-ngan")]   // dưới 32 ký tự ⇒ coi như chưa cấu hình
    public async Task KeyNotConfigured_RejectsEveryone_FailClosed(string? configured)
    {
        // Mặc định MỞ khi thiếu cấu hình chính là lỗi đang được sửa. Kể cả người gọi đưa đúng cái
        // chuỗi yếu đó cũng không được qua.
        var resp = await Handler(configured).Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = configured },
            CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        resp.Data.Should().BeNull();
    }

    [Fact]
    public async Task UnauthorizedCall_DoesNotTouchJwtValidationOrRedis()
    {
        // Chặn NGAY ĐẦU: từ chối sau khi đã kiểm chữ ký thì vẫn còn nguyên đường khuếch đại tải
        // (mỗi request ép một lần verify JWT + một truy vấn Redis), và thời gian phản hồi vẫn
        // rò rỉ thông tin về token.
        await Handler().Handle(
            new IntrospectTokenCommand { Token = RealToken(), PresentedApiKey = "sai" },
            CancellationToken.None);

        _jwt.Verify(j => j.ValidateToken(It.IsAny<string>()), Times.Never);
        _revocation.Verify(r => r.IsRevokedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void PresentedApiKey_CannotBeSetFromRequestBody()
    {
        // Nếu client tự đặt được khoá qua body thì lớp bảo vệ tự mở cửa cho đúng kẻ cần chặn.
        var property = typeof(IntrospectTokenCommand).GetProperty(nameof(IntrospectTokenCommand.PresentedApiKey))!;

        property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), inherit: true)
            .Should().NotBeEmpty("phải có [JsonIgnore] để không deserialize từ body");
        property.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), inherit: true)
            .Should().NotBeEmpty("phải có [BindNever] để model binder không gán từ form/query");
    }
}
