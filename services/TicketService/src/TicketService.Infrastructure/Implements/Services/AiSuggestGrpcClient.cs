using AiModule.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// BE-AI — gRPC impl của SuggestStaff. Gọi AiService.SuggestStaff trên :50051.
/// </summary>
/// <remarks>
/// Catch RpcException/Exception → trả <c>null</c>: gợi ý là tính năng phụ trợ, AI không
/// phản hồi được thì Manager vẫn phải triage được ticket như trước. Không bao giờ chặn
/// luồng nghiệp vụ (cùng chính sách với <see cref="AiTicketVerifyGrpcClient"/>).
/// </remarks>
public class AiStaffSuggestGrpcClient : IAiStaffSuggestClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly ILogger<AiStaffSuggestGrpcClient> _logger;
    private readonly int _timeoutSeconds;

    public AiStaffSuggestGrpcClient(
        AiService.AiServiceClient client,
        ILogger<AiStaffSuggestGrpcClient> logger,
        int timeoutSeconds)
    {
        _client = client;
        _logger = logger;
        _timeoutSeconds = timeoutSeconds;
    }

    public async Task<AiStaffSuggestResult?> SuggestStaffAsync(
        int category,
        int priority,
        string description,
        IReadOnlyList<AiStaffCandidate> candidates,
        int topN,
        CancellationToken ct)
    {
        try
        {
            var request = new SuggestStaffRequest
            {
                Category = category,
                Priority = priority,
                Description = description ?? string.Empty,
                TopN = topN
            };

            foreach (var c in candidates)
            {
                var candidate = new StaffCandidate
                {
                    StaffId = c.StaffId,
                    FullName = c.FullName ?? string.Empty,
                    SkillTier = c.SkillTier,
                    ActiveTickets = c.ActiveTickets,
                    MaxConcurrent = c.MaxConcurrent
                };
                candidate.SkillCodes.AddRange(c.SkillCodes);
                request.Candidates.Add(candidate);
            }

            var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            var resp = await _client.SuggestStaffAsync(request, deadline: deadline, cancellationToken: ct);

            return new AiStaffSuggestResult(
                resp.Suggestions
                    .Select(s => new AiStaffSuggestion(s.StaffId, s.FullName, s.Score, s.Reason, s.TierOk))
                    .ToList(),
                resp.Note);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "AI SuggestStaff RPC lỗi ({Status}) — bỏ qua gợi ý.", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI SuggestStaff lỗi — bỏ qua gợi ý.");
            return null;
        }
    }
}

/// <summary>
/// BE-AI — gRPC impl của SuggestKb. Gọi AiService.SuggestKb trên :50051.
/// Cùng chính sách fail-safe với <see cref="AiStaffSuggestGrpcClient"/>.
/// </summary>
public class AiKbSuggestGrpcClient : IAiKbSuggestClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly ILogger<AiKbSuggestGrpcClient> _logger;
    private readonly int _timeoutSeconds;

    public AiKbSuggestGrpcClient(
        AiService.AiServiceClient client,
        ILogger<AiKbSuggestGrpcClient> logger,
        int timeoutSeconds)
    {
        _client = client;
        _logger = logger;
        _timeoutSeconds = timeoutSeconds;
    }

    public async Task<AiKbSuggestResult?> SuggestKbAsync(
        int category,
        string description,
        IReadOnlyList<AiKbCandidate> candidates,
        int topN,
        IReadOnlyList<string> aiActionSteps,
        IReadOnlyList<string> aiSopReferences,
        IReadOnlyList<string> aiKbDocRefs,
        CancellationToken ct)
    {
        try
        {
            var request = new SuggestKbRequest
            {
                Category = category,
                Description = description ?? string.Empty,
                TopN = topN
            };
            request.AiActionSteps.AddRange(aiActionSteps);
            request.AiSopReferences.AddRange(aiSopReferences);
            request.AiKbDocRefs.AddRange(aiKbDocRefs);

            foreach (var c in candidates)
            {
                var candidate = new KbCandidate
                {
                    KbId = c.KbId,
                    Code = c.Code ?? string.Empty,
                    Title = c.Title ?? string.Empty,
                    Category = c.Category,
                    HelpfulCount = c.HelpfulCount
                };
                candidate.Tags.AddRange(c.Tags);
                request.Candidates.Add(candidate);
            }

            var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            var resp = await _client.SuggestKbAsync(request, deadline: deadline, cancellationToken: ct);

            return new AiKbSuggestResult(
                resp.Suggestions
                    .Select(s => new AiKbSuggestion(s.KbId, s.Code, s.Title, s.Score, s.Reason))
                    .ToList(),
                resp.Note);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "AI SuggestKb RPC lỗi ({Status}) — bỏ qua gợi ý.", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI SuggestKb lỗi — bỏ qua gợi ý.");
            return null;
        }
    }
}
