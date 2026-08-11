using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class GoogleCallbackCommandHandler : IRequestHandler<GoogleCallbackCommand, LoginResponse>
{
    private readonly IMediator _mediator;
    private readonly IGoogleOAuthHelper _googleOAuthHelper;
    private readonly IConfiguration _configuration;

    public GoogleCallbackCommandHandler(IMediator mediator, IGoogleOAuthHelper googleOAuthHelper, IConfiguration configuration)
    {
        _mediator = mediator;
        _googleOAuthHelper = googleOAuthHelper;
        _configuration = configuration;
    }

    public async Task<LoginResponse> Handle(GoogleCallbackCommand request, CancellationToken cancellationToken)
    {
        var allowedUris = _configuration
            .GetSection("GoogleOAuth:AllowedRedirectUris")
            .GetChildren()
            .Select(c => c.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowedUris.Count > 0 && !allowedUris.Contains(request.RedirectUri))
            return Fail(400, "Invalid RedirectUri.");

        var idToken = await _googleOAuthHelper.ExchangeCodeForIdTokenAsync(request.Code, request.RedirectUri, cancellationToken);
        if (string.IsNullOrWhiteSpace(idToken))
            return Fail(401, "Failed to exchange authorization code for id_token from Google.");

        return await _mediator.Send(new GoogleAuthCommand { IdToken = idToken }, cancellationToken);
    }

    private static LoginResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
