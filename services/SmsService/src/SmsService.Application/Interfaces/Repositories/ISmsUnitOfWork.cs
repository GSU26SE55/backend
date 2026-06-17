using SharedKernels.Interfaces;
using SmsService.Domain.Entities;

namespace SmsService.Application.Interfaces.Repositories;

/// <summary>
/// UoW cho SmsService — expose 4 repository tương ứng 4 bảng do service sở hữu.
/// Naming khớp với pattern <c>IAuthUnitOfWork</c> của AuthService.
/// </summary>
public interface ISmsUnitOfWork : IUnitOfWork
{
    IGenericRepository<SmsMessage> SmsMessages { get; }
    IGenericRepository<SmsGatewayDevice> SmsGatewayDevices { get; }
    IGenericRepository<SmsAuditLog> SmsAuditLogs { get; }
    IGenericRepository<OutboxMessage> OutboxMessages { get; }
}
