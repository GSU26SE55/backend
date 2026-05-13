using FileStorageService.Domain.Entities;
using SharedKernels.Interfaces;

namespace FileStorageService.Application.Interfaces;

public interface IFileStorageUnitOfWork : IUnitOfWork
{
    IGenericRepository<UploadedFile> UploadedFiles { get; }
}
