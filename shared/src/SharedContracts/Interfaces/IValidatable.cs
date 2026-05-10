using SharedContracts.Common.Responses;

namespace SharedContracts.Interfaces;

public interface IValidatable<TResponse> where TResponse : CommonResponseBase
{
    Task<TResponse> ValidateAsync();
}
