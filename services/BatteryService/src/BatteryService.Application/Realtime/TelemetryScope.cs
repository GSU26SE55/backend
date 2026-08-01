namespace BatteryService.Application.Realtime;

/// <summary>Sprint BE-IoT-Realtime — loại scope subscribe SSE (§34.10.3/34.10.5).</summary>
public enum TelemetryScopeType
{
    /// <summary>1 hoặc nhiều pin cụ thể. 1 pin → event <c>reading</c> đầy đủ; nhiều pin → <c>summary</c>.</summary>
    Asset = 1,
    /// <summary>Mọi pin của 1 khách — <c>summary</c>.</summary>
    Customer = 2,
    /// <summary>Mọi pin trong 1 hoặc nhiều site — <c>summary</c>.</summary>
    Site = 3,
    /// <summary>Mọi pin theo 1 loại pin (BatteryTypeId) — <c>summary</c>.</summary>
    BatteryType = 4,
    /// <summary>TOÀN hệ thống — <c>summary</c> (chỉ Admin/Manager).</summary>
    All = 5,
    /// <summary>Pin KHÔNG thuộc site nào (SiteId=null) — <c>summary</c> (chỉ Admin/Manager).</summary>
    SiteNone = 6
}

/// <summary>
/// Scope của một kết nối SSE. Hỗ trợ:
/// <c>asset:{id}</c> · <c>assets:{id1,id2}</c> · <c>customer:{id}</c> ·
/// <c>site:{id}</c> · <c>sites:{id1,id2}</c> · <c>type:{id}</c> · <c>all</c> · <c>site:none</c>.
/// </summary>
public readonly record struct TelemetryScope(TelemetryScopeType Kind, IReadOnlyList<Guid> Ids)
{
    /// <summary>Chặn list quá dài (DoS / browser ngợp).</summary>
    public const int MaxIds = 50;

    /// <summary>Chỉ 1 pin đơn → stream <c>reading</c> đầy đủ; còn lại → <c>summary</c> gom.</summary>
    public bool IsSingleAsset => Kind == TelemetryScopeType.Asset && Ids.Count == 1;

    /// <summary>
    /// Nhãn scope chuẩn — KHỚP từ khóa query <c>?scope=</c> (asset/customer/site/type/all/site:none).
    /// Dùng cho field <c>BatterySummaryDto.ScopeType</c> + nhãn metric. KHÔNG dùng <c>Kind.ToString()</c>
    /// (sinh "batterytype"/"sitenone" lệch keyword).
    /// </summary>
    public string Label => Kind switch
    {
        TelemetryScopeType.Asset => "asset",
        TelemetryScopeType.Customer => "customer",
        TelemetryScopeType.Site => "site",
        TelemetryScopeType.BatteryType => "type",
        TelemetryScopeType.All => "all",
        TelemetryScopeType.SiteNone => "site:none",
        _ => Kind.ToString().ToLowerInvariant()
    };

    /// <summary>Parse query <c>?scope=...</c>. Trả null nếu sai format / list rỗng / vượt <see cref="MaxIds"/>.</summary>
    public static TelemetryScope? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim();
        var lower = raw.ToLowerInvariant();

        // Scope không cần id.
        if (lower == "all")
            return new TelemetryScope(TelemetryScopeType.All, Array.Empty<Guid>());
        if (lower == "site:none")
            return new TelemetryScope(TelemetryScopeType.SiteNone, Array.Empty<Guid>());

        var sep = raw.IndexOf(':');
        if (sep <= 0 || sep == raw.Length - 1)
            return null;

        var prefix = raw[..sep].Trim().ToLowerInvariant();
        var rest = raw[(sep + 1)..].Trim();

        TelemetryScopeType kind;
        bool allowMulti;
        switch (prefix)
        {
            case "asset":
                kind = TelemetryScopeType.Asset;
                allowMulti = false;
                break;
            case "assets":
                kind = TelemetryScopeType.Asset;
                allowMulti = true;
                break;
            case "customer":
                kind = TelemetryScopeType.Customer;
                allowMulti = false;
                break;
            case "site":
                kind = TelemetryScopeType.Site;
                allowMulti = false;
                break;
            case "sites":
                kind = TelemetryScopeType.Site;
                allowMulti = true;
                break;
            case "type":
                kind = TelemetryScopeType.BatteryType;
                allowMulti = false;
                break;
            default:
                return null;
        }

        var ids = new List<Guid>();
        foreach (var part in rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
                return null;
            ids.Add(id);
        }

        if (ids.Count == 0 || ids.Count > MaxIds)
            return null;
        if (!allowMulti && ids.Count != 1)
            return null; // asset:/customer:/site:/type: chỉ nhận đúng 1 id (multi dùng assets:/sites:)

        return new TelemetryScope(kind, ids);
    }
}

/// <summary>Một message SSE đã sẵn sàng ghi xuống client: <c>event: {Event}\n data: {Data}</c>.</summary>
/// <param name="Event">Tên event SSE (`reading` | `summary` | `stats` | `ping`).</param>
/// <param name="Data">Payload JSON.</param>
/// <param name="Id">
/// Sprint BE-IoT-Realtime <c>#614</c> — id của sự kiện, ghi ra dòng <c>id:</c> để trình duyệt nhớ và
/// gửi lại qua header <c>Last-Event-ID</c> khi reconnect.
///
/// <para><b>Chỉ set cho `reading` ở scope 1 pin</b> — đó là chỗ duy nhất replay được (xem
/// <c>RedisTelemetryStream</c>). `summary` là ảnh chụp định kỳ latest-per-asset: bỏ lỡ vài nhịp
/// không mất dữ liệu vì nhịp kế tiếp (≤ SummaryIntervalSeconds) đã mang trạng thái hiện tại của mọi
/// pin. `ping`/`stats` cũng không cần phát lại. KHÔNG phát <c>id:</c> ở những chỗ không honor được —
/// gửi id rồi lờ đi khi client resume còn tệ hơn không gửi.</para>
/// </param>
public readonly record struct SseMessage(string Event, string Data, string? Id = null);
