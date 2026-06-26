using AuthService.Domain.Enums;
using SharedContracts.Audit;

namespace AuthService.Application.CQRS.Handler.Audit;

/// <summary>
/// Map <see cref="AuditActionEnum"/> → (ActionCode, Category, Severity) chuẩn Hybrid Audit (Sprint audit #AUDIT-09).
/// Heuristic theo tên action; có thể tinh chỉnh per-action sau. ActionCode = tên enum (PascalCase, quá khứ).
/// </summary>
public static class AuditActionClassifier
{
    public static (string ActionCode, string Category, string Severity) Classify(AuditActionEnum action, bool isSuccess)
    {
        var code = action.ToString();
        var name = code.ToLowerInvariant();

        var category = name switch
        {
            _ when name.Contains("login") || name.Contains("logout") || name.Contains("otp")
                   || name.Contains("twofactor") || name.Contains("token") || name.Contains("backupcode")
                   || name.Contains("verified") || name.Contains("google") => AuditCategories.Authentication,
            _ when name.Contains("role") || name.Contains("permission") => AuditCategories.Authorization,
            _ when name.Contains("password") || name.Contains("reset") => AuditCategories.AccountManagement,
            _ when name.Contains("lock") || name.Contains("ban") || name.Contains("suspend")
                   || name.Contains("admin") => AuditCategories.AccountManagement,
            _ => AuditCategories.AccountManagement,
        };

        // Security-sensitive → Security; thất bại → Warning; còn lại Info.
        var severity =
            name.Contains("permission") || name.Contains("role") || name.Contains("reuse")
            || name.Contains("ban") || name.Contains("suspend") || name.Contains("autolock")
                ? Severities.Security
            : !isSuccess
                ? Severities.Warning
                : Severities.Info;

        return (code, category, severity);
    }
}
