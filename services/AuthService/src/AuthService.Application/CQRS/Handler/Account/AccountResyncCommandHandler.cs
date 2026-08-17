using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// 02/08/2026 — Phát lại <see cref="AccountSyncSnapshotEvent"/> cho account để service tiêu thụ
/// dựng lại read-model. Xem <see cref="AccountResyncCommand"/> cho lý do tồn tại.
/// </summary>
public class AccountResyncCommandHandler : IRequestHandler<AccountResyncCommand, AccountResyncResponse>
{
    /// <summary>
    /// Số snapshot ghi vào outbox trước mỗi lần <c>SaveChangesAsync</c>. Có lô để một lần đối soát
    /// trên hệ thống nhiều tài khoản không dồn toàn bộ OutboxMessage vào một transaction duy nhất.
    /// </summary>
    private const int BatchSize = 500;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public AccountResyncCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<AccountResyncResponse> Handle(AccountResyncCommand request, CancellationToken cancellationToken)
    {
        // KHÔNG lọc !IsDeleted: account đã xoá mềm cũng phải được phát snapshot, nếu không thì
        // read-model bên kia không có cách nào biết mà đánh dấu xoá theo.
        // GetAllAsync là SYNC — trả IQueryable trực tiếp.
        //
        // IgnoreQueryFilters là BẮT BUỘC, không phải phòng hờ: AccountConfiguration đặt
        // HasQueryFilter(a => !a.IsDeleted), nên nếu thiếu thì EF loại account đã xoá một cách im
        // lặng và ý định ở comment trên không bao giờ thành hiện thực — mọi lượt đối soát đều báo
        // DeletedAccounts = 0 và không sửa được read-model nào đang giữ account đã xoá.
        var query = _unitOfWork.Accounts.GetAllAsync().IgnoreQueryFilters().Include(a => a.Role);

        var accounts = request.AccountId.HasValue
            ? await query.Where(a => a.Id == request.AccountId.Value).ToListAsync(cancellationToken)
            : await query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);

        if (request.AccountId.HasValue && accounts.Count == 0)
        {
            return new AccountResyncResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Account not found."
            };
        }

        // Một mốc duy nhất cho cả lượt: consumer dùng mốc này để loại snapshot về trễ, nên mọi
        // snapshot của cùng một lượt đối soát phải cùng mốc thì mới không tự loại lẫn nhau.
        var snapshotAtUtc = DateTime.UtcNow;
        var pending = 0;

        foreach (var account in accounts)
        {
            await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
                account.Id,
                account.Email,
                account.FullName,
                account.PhoneNumber,
                account.Role?.Name ?? string.Empty,
                IsActive: account.Status.IsNotifiable() && !account.IsDeleted,
                IsDeleted: account.IsDeleted,
                SnapshotAtUtc: snapshotAtUtc,
                Reason: "Resync"), cancellationToken);

            if (++pending >= BatchSize)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                pending = 0;
            }
        }

        if (pending > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        var active = accounts.Count(a => a.Status.IsNotifiable() && !a.IsDeleted);
        var deleted = accounts.Count(a => a.IsDeleted);

        return new AccountResyncResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Published {accounts.Count} account sync snapshot(s).",
            Data = new AccountResyncDto
            {
                TotalAccounts = accounts.Count,
                ActiveAccounts = active,
                InactiveAccounts = accounts.Count - active - deleted,
                DeletedAccounts = deleted
            }
        };
    }
}
