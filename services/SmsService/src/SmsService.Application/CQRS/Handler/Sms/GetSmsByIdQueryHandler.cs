using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Query.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;

namespace SmsService.Application.CQRS.Handler.Sms;

public class GetSmsByIdQueryHandler : IRequestHandler<GetSmsByIdQuery, CommonResponse<SmsMessage>>
{
    private readonly ISmsUnitOfWork _unitOfWork;

    public GetSmsByIdQueryHandler(ISmsUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SmsMessage>> Handle(GetSmsByIdQuery request, CancellationToken cancellationToken)
    {
        var sms = await _unitOfWork.SmsMessages
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.SmsId && !x.IsDeleted, cancellationToken);

        if (sms is null)
            return new CommonResponse<SmsMessage>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "SMS not found.",
                Data = null
            };

        return new CommonResponse<SmsMessage>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OK",
            Data = sms
        };
    }
}
