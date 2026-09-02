using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Persistence.Converters;

/// <summary>
/// Keeps the legacy TicketService database representation (1..6) while the domain and integration
/// event contract use the AuthService representation (0..5).
/// </summary>
/// <remarks>
/// Production can automatically roll an image back without rolling its database migrations back.
/// Preserving the storage representation makes both the previous image and the aligned image safe
/// against the same rows during deployment and rollback. Unknown values fail closed as Inactive.
/// </remarks>
public sealed class AccountStatusStorageConverter : ValueConverter<AccountStatusEnum, int>
{
    public AccountStatusStorageConverter()
        : base(status => ToStorage(status), stored => FromStorage(stored))
    {
    }

    private static int ToStorage(AccountStatusEnum status) => status switch
    {
        AccountStatusEnum.PendingVerification => 1,
        AccountStatusEnum.Active => 2,
        AccountStatusEnum.Locked => 3,
        AccountStatusEnum.Inactive => 4,
        AccountStatusEnum.Suspended => 5,
        AccountStatusEnum.Banned => 6,
        _ => 4,
    };

    private static AccountStatusEnum FromStorage(int stored) => stored switch
    {
        1 => AccountStatusEnum.PendingVerification,
        2 => AccountStatusEnum.Active,
        3 => AccountStatusEnum.Locked,
        4 => AccountStatusEnum.Inactive,
        5 => AccountStatusEnum.Suspended,
        6 => AccountStatusEnum.Banned,
        _ => AccountStatusEnum.Inactive,
    };
}
