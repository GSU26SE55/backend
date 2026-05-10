using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TResponse : CommonResponseBase
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IValidatable<TResponse> validatable)
        {
            _logger.LogInformation("[Pipeline] Validating logic for {RequestType}", typeof(TRequest).Name);

            var result = await validatable.ValidateAsync();

            if (!result.IsSuccess)
            {
                _logger.LogWarning("[Pipeline] Validation failed for {RequestType}", typeof(TRequest).Name);
                return result;
            }
        }

        return await next();
    }
}
