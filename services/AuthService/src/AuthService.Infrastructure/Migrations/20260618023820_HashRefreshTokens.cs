using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// #AUTH-01: backfill — chuyển toàn bộ refresh_tokens.token sang SHA-256 hex.
    /// Clients đang giữ plaintext refresh token vẫn dùng được, vì application layer cũng đã được sửa
    /// để hash plaintext incoming → so sánh với hash trong DB.
    ///
    /// Down: không thể đảo ngược hash. Để rollback an toàn, revoke toàn bộ token và buộc user re-login.
    /// </summary>
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL: encode(sha256(text::bytea), 'hex') trả lowercase hex 64 chars,
            // khớp với RefreshTokenHasher.Hash(...) ở Application layer.
            // Idempotent guard: chỉ hash row nào còn plaintext (length != 64 hoặc chứa char không phải hex).
            migrationBuilder.Sql(@"
                UPDATE refresh_tokens
                SET token = encode(sha256(token::bytea), 'hex')
                WHERE token IS NOT NULL
                  AND (length(token) <> 64 OR token !~ '^[0-9a-f]{64}$');
            ");

            // Cũng hash cột replaced_by_token (audit chain). Bỏ sót sẽ leak plaintext lúc DB dump
            // dù field này không dùng để lookup.
            migrationBuilder.Sql(@"
                UPDATE refresh_tokens
                SET replaced_by_token = encode(sha256(replaced_by_token::bytea), 'hex')
                WHERE replaced_by_token IS NOT NULL
                  AND (length(replaced_by_token) <> 64 OR replaced_by_token !~ '^[0-9a-f]{64}$');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hash là one-way. Khi rollback, revoke toàn bộ active token để buộc user re-login.
            migrationBuilder.Sql(@"
                UPDATE refresh_tokens
                SET status = 4,
                    revoked_at = NOW() AT TIME ZONE 'UTC',
                    revoked_reason = 'HashRefreshTokens migration rollback'
                WHERE status = 1;
            ");
        }
    }
}
