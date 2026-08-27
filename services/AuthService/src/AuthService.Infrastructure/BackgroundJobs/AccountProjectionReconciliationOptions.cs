using System.ComponentModel.DataAnnotations;

namespace AuthService.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically republishes authoritative AuthService account snapshots so downstream
/// projections converge even when an earlier message was missed or a read model was edited.
/// </summary>
public sealed class AccountProjectionReconciliationOptions
{
    public const string SectionName = "AccountProjectionSync";

    public bool Enabled { get; set; }

    [Range(0, 3600)]
    public int InitialDelaySeconds { get; set; } = 30;

    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 5;
}
