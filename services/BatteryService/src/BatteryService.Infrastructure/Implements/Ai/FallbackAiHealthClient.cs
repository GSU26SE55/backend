using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — composite Health client: cache → gRPC PRIMARY → HTTP FALLBACK → null.
/// </summary>
/// <remarks>
/// <para>
/// Cache là bắt buộc chứ không phải tối ưu vặt: caller gọi mỗi lượt job (mỗi
/// <c>Ai:IntervalMinutes</c>) chỉ để biết <c>soc_mode</c> và <c>lfp_loaded</c> — hai thứ
/// chỉ đổi khi AI được deploy lại. Không cache thì mỗi lượt tốn thêm một round-trip mà
/// câu trả lời luôn giống hệt.
/// </para>
/// <para>
/// TTL ngắn (<see cref="CacheTtl"/>) chứ không cache vĩnh viễn: sau khi AI được rebuild
/// (vd bổ sung bộ artifact LFP), BE phải tự nhận ra trong vòng vài phút mà không cần
/// restart. Cache CHỈ giữ kết quả THÀNH CÔNG — lỗi không được cache, nếu không một lần
/// AI chớp tắt sẽ khoá BE vào trạng thái "không biết" suốt cả TTL.
/// </para>
/// </remarks>
public class FallbackAiHealthClient : IAiHealthClient
{
    /// <summary>Health đổi theo nhịp deploy, không theo nhịp request.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly AiHealthGrpcClient _grpc;
    private readonly AiHealthHttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<FallbackAiHealthClient> _logger;
    private readonly TimeProvider _timeProvider;

    // static: client được đăng ký Scoped nên mỗi lượt job là một instance mới —
    // cache theo instance sẽ không bao giờ trúng. SemaphoreSlim để hai lượt job
    // chồng nhau không cùng bắn request.
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static AiHealthResult? _cached;
    private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public FallbackAiHealthClient(
        AiHealthGrpcClient grpc,
        AiHealthHttpClient http,
        IOptions<AiOptions> options,
        ILogger<FallbackAiHealthClient> logger,
        TimeProvider? timeProvider = null)
    {
        _grpc = grpc;
        _http = http;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Xoá cache — chỉ dùng trong test để các case không rò trạng thái sang nhau.</summary>
    public static void ResetCacheForTests()
    {
        _cached = null;
        _cachedAt = DateTimeOffset.MinValue;
    }

    public async Task<AiHealthResult?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cached is not null && now - _cachedAt < CacheTtl)
            return _cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Kiểm lại sau khi vào cổng: lượt trước có thể vừa làm mới xong.
            now = _timeProvider.GetUtcNow();
            if (_cached is not null && now - _cachedAt < CacheTtl)
                return _cached;

            var fresh = await FetchAsync(cancellationToken);
            if (fresh is not null)
            {
                _cached = fresh;
                _cachedAt = now;
            }
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AiHealthResult?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _grpc.GetHealthAsync(_options.TimeoutSeconds, cancellationToken);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                "gRPC AI Health không gọi được ({Code}) — thử HTTP.", ex.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "gRPC AI Health lỗi — thử HTTP.");
        }

        try
        {
            return await _http.GetHealthAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // KHÔNG nâng lên Error: job vẫn chạy được khi không biết health, chỉ là
            // phải dùng đường suy luận an toàn hơn (xem SohPredictionBackgroundService).
            _logger.LogWarning(ex, "HTTP AI Health cũng lỗi — coi như chưa biết trạng thái AI.");
            return null;
        }
    }
}
