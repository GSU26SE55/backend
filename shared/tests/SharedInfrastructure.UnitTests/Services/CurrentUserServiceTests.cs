using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SharedInfrastructure.Services;

namespace SharedInfrastructure.UnitTests.Services;

public class CurrentUserServiceTests
{
    private readonly Mock<IHttpContextAccessor> _accessor = new();

    private CurrentUserService Sut() => new(_accessor.Object);

    [Fact]
    public void UserId_NullHttpContext_ReturnsNull()
    {
        _accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        Sut().UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_NoNameIdentifierClaim_ReturnsNull()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        _accessor.Setup(a => a.HttpContext).Returns(ctx);

        Sut().UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_HasNameIdentifierClaim_ReturnsValue()
    {
        var userId = Guid.NewGuid().ToString();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            }))
        };
        _accessor.Setup(a => a.HttpContext).Returns(ctx);

        Sut().UserId.Should().Be(userId);
    }
}
