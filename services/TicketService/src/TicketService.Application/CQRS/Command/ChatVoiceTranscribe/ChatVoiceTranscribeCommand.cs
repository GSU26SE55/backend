using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatVoiceTranscribe;

public class ChatVoiceTranscribeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// Max audio file size default.
    /// </summary>
    public const long MaxAudioFileSizeDefault = 20_971_520; // 20MB — Gemini inline limit

    private static readonly HashSet<string> AllowedAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // MP3
        "audio/mpeg", "audio/mp3",
        // WAV
        "audio/wav", "audio/x-wav", "audio/wave",
        // OGG
        "audio/ogg",
        // WebM — browser đôi khi gửi video/webm cho file .webm audio
        "audio/webm", "video/webm",
        // M4A / AAC
        "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/aac",
        // FLAC
        "audio/flac", "audio/x-flac",
    };

    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của người dùng.
    /// </summary>
    [JsonIgnore]
    public Guid UserId { get; set; }

    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }

    /// <summary>
    /// Tên hiển thị của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách quyền hạn của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    /// <summary>
    /// Tệp tin âm thanh tải lên.
    /// </summary>
    [JsonIgnore]
    public IFormFile? AudioFile { get; set; }

    /// <summary>
    /// Header Authorization (JWT token) đi kèm.
    /// </summary>
    [JsonIgnore]
    public string AuthorizationHeader { get; set; } = string.Empty;

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (AudioFile is null || AudioFile.Length <= 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors { Field = "audioFile", Detail = "File audio là bắt buộc." });
            return Task.FromResult(response);
        }

        if (!AllowedAudioMimeTypes.Contains(AudioFile.ContentType))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors
            {
                Field = "audioFile",
                Detail = $"Định dạng audio không hợp lệ (nhận được: {AudioFile.ContentType}). Chấp nhận: mp3, wav, ogg, webm, m4a, flac."
            });
        }

        if (AudioFile.Length > MaxAudioFileSizeDefault)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors
            {
                Field = "audioFile",
                Detail = "File audio vượt quá giới hạn 20 MB."
            });
        }

        return Task.FromResult(response);
    }
}
