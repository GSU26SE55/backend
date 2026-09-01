using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Invite;

public class AcceptInviteCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtHelper> _jwt = new();
    private readonly Mock<IMessageProducerService> _producer = new();
    private readonly Mock<MediatR.IPublisher> _publisher = MockPublisher.NoOp();

    public AcceptInviteCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("NEW-HASH");
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<global::AuthService.Domain.Entities.Account>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>?>())).ReturnsAsync("access");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
    }

    private static global::AuthService.Domain.Entities.Account InvitedAccount(string token = "good-token", DateTime? expiresAt = null)
    {
        return new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "invited@example.com",
            PasswordHash = "PLACEHOLDER",
            FullName = "Invited User",
            Status = AccountStatusEnum.PendingVerification,
            EmailConfirmed = false,
            InvitationToken = token,
            InvitationExpiredAt = expiresAt ?? DateTime.UtcNow.AddHours(20),
            RoleId = Guid.NewGuid()
        };
    }

    [Fact]
    public async Task Accept_ValidToken_ActivatesAccount_SetsPassword_ClearsToken_IssuesTokens()
    {
        var account = InvitedAccount();
        var (uow, _, refreshTokens, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new AcceptInviteCommandHandler(uow.Object, _hasher.Object, _jwt.Object, _producer.Object, _publisher.Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));
        var resp = await handler.Handle(new AcceptInviteCommand
        {
            InvitationToken = "good-token",
            Password = "Strong1Pass!",
            ConfirmPassword = "Strong1Pass!"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data!.Tokens!.AccessToken.Should().Be("access");
        resp.Data.Tokens!.RefreshToken.Should().Be("refresh");

        account.PasswordHash.Should().Be("NEW-HASH");
        account.Status.Should().Be(AccountStatusEnum.Active);
        account.EmailConfirmed.Should().BeTrue();
        account.InvitationToken.Should().BeNull();
        account.InvitationExpiredAt.Should().BeNull();
        account.LastLoginAt.Should().NotBeNull();

        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>()), Times.Once);
        _producer.Verify(p => p.PublishAsync(
            It.Is<AccountActivatedEvent>(e => e.CreationSource == "AdminInvite"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accept_InvalidToken_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new AcceptInviteCommandHandler(uow.Object, _hasher.Object, _jwt.Object, _producer.Object, _publisher.Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new AcceptInviteCommand
        {
            InvitationToken = "wrong-token",
            Password = "Strong1Pass!",
            ConfirmPassword = "Strong1Pass!"
        }, CancellationToken.None);

        // #38 QA solars.io.vn 2026-08-29: business error trên luồng chưa đăng nhập không được
        // dùng 401 — axios.ts coi mọi 401 != TOKEN_EXPIRED là hết phiên và tự logout.
        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Accept_ExpiredToken_Returns400_DoesNotActivate()
    {
        var account = InvitedAccount(expiresAt: DateTime.UtcNow.AddHours(-1));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new AcceptInviteCommandHandler(uow.Object, _hasher.Object, _jwt.Object, _producer.Object, _publisher.Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));
        var resp = await handler.Handle(new AcceptInviteCommand
        {
            InvitationToken = "good-token",
            Password = "Strong1Pass!",
            ConfirmPassword = "Strong1Pass!"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
        account.Status.Should().Be(AccountStatusEnum.PendingVerification);
    }

    [Fact]
    public async Task Accept_AccountAlreadyActive_Returns400()
    {
        var account = InvitedAccount();
        account.Status = AccountStatusEnum.Active;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new AcceptInviteCommandHandler(uow.Object, _hasher.Object, _jwt.Object, _producer.Object, _publisher.Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));
        var resp = await handler.Handle(new AcceptInviteCommand
        {
            InvitationToken = "good-token",
            Password = "Strong1Pass!",
            ConfirmPassword = "Strong1Pass!"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }
}
