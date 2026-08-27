using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

public class GoogleAuthCommandHandlerTests
{
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IJwtHelper> _jwt = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IGoogleOAuthHelper> _google = new();

    public GoogleAuthCommandHandlerTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<Account>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>?>())).ReturnsAsync("acc");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("rt");
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASH");
    }

    [Fact]
    public async Task Google_NewEmail_CreatesAccount_AssignsCustomerRole_IssuesTokens()
    {
        _google.Setup(g => g.ValidateAsync("good-token", It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "new@gmail.com",
            EmailVerified = true,
            Name = "New User",
            Subject = "google-sub-1"
        });
        var customerRole = new Role { Id = CustomerRoleId, Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active };
        var (uow, accounts, refreshTokens, _) = MockUnitOfWork.Build(roleSeed: new[] { customerRole });
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "good-token" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accounts.Verify(r => r.AddAsync(It.Is<Account>(a =>
            a.Email == "new@gmail.com" &&
            a.GoogleId == "google-sub-1" &&
            a.Provider == "Google" &&
            a.EmailConfirmed &&
            a.Status == AccountStatusEnum.Active &&
            a.RoleId == CustomerRoleId
        )), Times.Once);
        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>()), Times.Once);
    }

    [Fact]
    public async Task Google_NewEmail_StoresGooglePictureInAccountProfile()
    {
        _google.Setup(g => g.ValidateAsync("good-token", It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "new@gmail.com",
            EmailVerified = true,
            Name = "New User",
            Subject = "google-sub-1",
            Picture = "https://lh3.googleusercontent.com/a/avatar"
        });
        var customerRole = new Role { Id = CustomerRoleId, Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active };
        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { customerRole });
        var accountProfiles = Mock.Get(uow.Object.AccountProfiles);
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "good-token" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accountProfiles.Verify(r => r.AddAsync(It.Is<AccountProfile>(profile =>
            profile.ExternalAvatarUrl == "https://lh3.googleusercontent.com/a/avatar" &&
            profile.AvatarFileId == null &&
            profile.AvatarSource == AvatarSourceEnum.Google)), Times.Once);
    }

    [Fact]
    public async Task Google_ExistingEmailConfirmed_AutoLinks()
    {
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            EmailConfirmed = true,
            RoleId = CustomerRoleId
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "user@gmail.com",
            EmailVerified = true,
            Subject = "google-sub-2"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        existing.GoogleId.Should().Be("google-sub-2");
        existing.Provider.Should().Be("Google");
    }

    [Fact]
    public async Task Google_ExistingLockedAccount_PublishesUnlockProjectionEvent()
    {
        var role = new Role
        {
            Id = CustomerRoleId,
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active
        };
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "locked@gmail.com",
            PasswordHash = "x",
            FullName = "Locked Customer",
            Status = AccountStatusEnum.Locked,
            EmailConfirmed = true,
            GoogleId = "google-locked",
            RoleId = role.Id,
            Role = role
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo
            {
                Email = existing.Email,
                EmailVerified = true,
                Subject = existing.GoogleId
            });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var producer = new Mock<IMessageProducerService>();
        var handler = new GoogleAuthCommandHandler(
            uow.Object, _jwt.Object, _hasher.Object, _google.Object, producer.Object,
            MockPublisher.NoOp().Object,
            Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var response = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        existing.Status.Should().Be(AccountStatusEnum.Active);
        producer.Verify(item => item.PublishAsync(
            It.Is<AccountStatusChangedEvent>(evt =>
                evt.AccountId == existing.Id
                && evt.NewStatus == (int)AccountStatusEnum.Active
                && evt.Role == "Customer"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Google_ExistingUploadedAvatar_DoesNotOverwriteUploadedAvatar()
    {
        var avatarFileId = Guid.NewGuid();
        var profile = new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            AvatarFileId = avatarFileId,
            AvatarSource = AvatarSourceEnum.Uploaded,
            ExternalAvatarUrl = "https://old.google/avatar"
        };
        var existing = new Account
        {
            Id = profile.AccountId,
            Email = "user@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            EmailConfirmed = true,
            Profile = profile,
            RoleId = CustomerRoleId
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "user@gmail.com",
            EmailVerified = true,
            Subject = "google-sub-2",
            Picture = "https://new.google/avatar"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing }, accountProfileSeed: new[] { profile });
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        profile.AvatarFileId.Should().Be(avatarFileId);
        profile.AvatarSource.Should().Be(AvatarSourceEnum.Uploaded);
        profile.ExternalAvatarUrl.Should().Be("https://new.google/avatar");
    }

    [Fact]
    public async Task Google_ExistingPendingVerification_Returns409()
    {
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "pending@gmail.com",
            PasswordHash = "x",
            FullName = "P",
            Status = AccountStatusEnum.PendingVerification,
            EmailConfirmed = false,
            RoleId = CustomerRoleId
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "pending@gmail.com",
            EmailVerified = true,
            Subject = "g3"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Google_ExistingDifferentGoogleId_Returns409()
    {
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            EmailConfirmed = true,
            GoogleId = "other-google-id",
            RoleId = CustomerRoleId
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "user@gmail.com",
            EmailVerified = true,
            Subject = "different-google-id"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Google_InvalidToken_Returns401()
    {
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GoogleUserInfo?)null);
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "bad" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Google_EmailNotVerified_Returns401()
    {
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "x@gmail.com",
            EmailVerified = false,
            Subject = "g"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GoogleAuthCommandHandler(uow.Object, _jwt.Object, _hasher.Object, _google.Object, new Mock<IMessageProducerService>().Object, MockPublisher.NoOp().Object, Microsoft.Extensions.Options.Options.Create(new AuthService.Application.Configuration.JwtSettingsOptions()));

        var resp = await handler.Handle(new GoogleAuthCommand { IdToken = "x" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }
}

public class LinkGoogleCommandHandlerTests
{
    private readonly Mock<IGoogleOAuthHelper> _google = new();

    [Fact]
    public async Task Link_EmailMatch_LinksGoogleId()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "u@gmail.com",
            EmailVerified = true,
            Subject = "g-1"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new LinkGoogleCommandHandler(uow.Object, _google.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new LinkGoogleCommand { AccountId = account.Id, IdToken = "x" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.GoogleId.Should().Be("g-1");
        account.Provider.Should().Be("Google");
    }

    [Fact]
    public async Task Link_EmailMismatch_Returns400()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "real@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "different@gmail.com",
            EmailVerified = true,
            Subject = "g"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new LinkGoogleCommandHandler(uow.Object, _google.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new LinkGoogleCommand { AccountId = account.Id, IdToken = "x" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Link_GoogleAlreadyLinkedToAnotherAccount_Returns409()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var another = new Account
        {
            Id = Guid.NewGuid(),
            Email = "another@gmail.com",
            PasswordHash = "x",
            FullName = "A",
            Status = AccountStatusEnum.Active,
            GoogleId = "shared-google-id"
        };
        _google.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoogleUserInfo
        {
            Email = "u@gmail.com",
            EmailVerified = true,
            Subject = "shared-google-id"
        });
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account, another });
        var handler = new LinkGoogleCommandHandler(uow.Object, _google.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new LinkGoogleCommand { AccountId = account.Id, IdToken = "x" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }
}

public class UnlinkGoogleCommandHandlerTests
{
    [Fact]
    public async Task Unlink_HasPasswordAndGoogleLinked_RemovesLink()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@gmail.com",
            PasswordHash = "real-hash",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            GoogleId = "g-1",
            Provider = "Google"
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UnlinkGoogleCommandHandler(uow.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new UnlinkGoogleCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.GoogleId.Should().BeNull();
        account.Provider.Should().BeNull();
    }

    [Fact]
    public async Task Unlink_NoGoogleLinked_Returns400()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@gmail.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            GoogleId = null
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UnlinkGoogleCommandHandler(uow.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new UnlinkGoogleCommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Unlink_NoPasswordHash_Returns400()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@gmail.com",
            PasswordHash = "",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            GoogleId = "g-1"
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UnlinkGoogleCommandHandler(uow.Object, Moq.Mock.Of<MediatR.IPublisher>());

        var resp = await handler.Handle(new UnlinkGoogleCommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }
}
