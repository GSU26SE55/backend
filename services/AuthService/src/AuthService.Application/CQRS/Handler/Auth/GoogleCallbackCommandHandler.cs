using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class GoogleCallbackCommandHandler : IRequestHandler<GoogleCallbackCommand, LoginResponse>
{
    private readonly IMediator _mediator;
    private readonly IGoogleOAuthHelper _googleOAuthHelper;

    public GoogleCallbackCommandHandler(IMediator mediator, IGoogleOAuthHelper googleOAuthHelper)
    {
        _mediator = mediator;
        _googleOAuthHelper = googleOAuthHelper;
    }

    public async Task<LoginResponse> Handle(GoogleCallbackCommand request, CancellationToken cancellationToken)
    {
        var idToken = await _googleOAuthHelper.ExchangeCodeForIdTokenAsync(request.Code, request.RedirectUri, cancellationToken);
        if (string.IsNullOrWhiteSpace(idToken))
            return Fail(401, "Không thể đổi authorization code lấy id_token từ Google.");

        return await _mediator.Send(new GoogleAuthCommand { IdToken = idToken }, cancellationToken);
    }

    private static LoginResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = new List<Errors> { new Errors { Field = "Auth", Detail = message } }
    };
}
