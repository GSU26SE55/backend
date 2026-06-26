using FileStorageService.Domain.Entities;
using SharedKernels.Interfaces;

namespace FileStorageService.Application.Interfaces;

public interface IFileStorageUnitOfWork : IUnitOfWork
{
    IGenericRepository<UploadedFile> UploadedFiles { get; }
    IGenericRepository<FileAuditLog> FileAuditLogs { get; }       // Sprint audit #AUDIT-29
    IGenericRepository<FileAuditOutbox> FileAuditOutboxes { get; } // Sprint audit #AUDIT-29
}
