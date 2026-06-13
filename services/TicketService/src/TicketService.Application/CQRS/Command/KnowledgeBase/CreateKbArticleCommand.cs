using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class CreateKbArticleCommand : IRequest<CommonResponse<KbArticleDto>>, IValidatable<CommonResponse<KbArticleDto>>
{
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }
    [JsonIgnore]
    public string CurrentUserRole { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public string? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();

    public Task<CommonResponse<KbArticleDto>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            ListErrors = new List<Errors>()
        };

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được để trống." });
        else if (Title.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được vượt quá 200 ký tự." });

        if (string.IsNullOrWhiteSpace(Symptoms))
            response.ListErrors.Add(new Errors { Field = "Symptoms", Detail = "Triệu chứng không được để trống." });
        else if (Symptoms.Length > 2000)
            response.ListErrors.Add(new Errors { Field = "Symptoms", Detail = "Triệu chứng không được vượt quá 2000 ký tự." });

        if (string.IsNullOrWhiteSpace(DiagnosisSteps))
            response.ListErrors.Add(new Errors { Field = "DiagnosisSteps", Detail = "Các bước chẩn đoán không được để trống." });
        else if (DiagnosisSteps.Length > 4000)
            response.ListErrors.Add(new Errors { Field = "DiagnosisSteps", Detail = "Các bước chẩn đoán không được vượt quá 4000 ký tự." });

        if (string.IsNullOrWhiteSpace(SolutionSteps))
            response.ListErrors.Add(new Errors { Field = "SolutionSteps", Detail = "Các bước xử lý không được để trống." });
        else if (SolutionSteps.Length > 4000)
            response.ListErrors.Add(new Errors { Field = "SolutionSteps", Detail = "Các bước xử lý không được vượt quá 4000 ký tự." });

        if (!Enum.IsDefined(typeof(TicketCategoryEnum), Category))
            response.ListErrors.Add(new Errors { Field = "Category", Detail = "Danh mục không hợp lệ." });

        if (Tags != null && Tags.Count > 10)
            response.ListErrors.Add(new Errors { Field = "Tags", Detail = "Tối đa 10 thẻ." });

        if (Tags != null && Tags.Any(t => t.Length > 50))
            response.ListErrors.Add(new Errors { Field = "Tags", Detail = "Mỗi thẻ không được vượt quá 50 ký tự." });

        if (!string.IsNullOrWhiteSpace(RecommendedParts))
        {
            try
            {
                JsonDocument.Parse(RecommendedParts);
            }
            catch
            {
                response.ListErrors.Add(new Errors { Field = "RecommendedParts", Detail = "Linh kiện khuyến nghị phải là định dạng JSON hợp lệ." });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
