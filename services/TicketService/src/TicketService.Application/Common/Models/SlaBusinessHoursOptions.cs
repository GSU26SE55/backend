namespace TicketService.Application.Common.Models;

public sealed class SlaBusinessHoursOptions
{
    public const string SectionName = "SlaBusinessHours";

    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public TimeSpan Start { get; set; } = TimeSpan.FromHours(7);
    public TimeSpan End { get; set; } = TimeSpan.FromHours(17);
    public DayOfWeek[] WorkingDays { get; set; } = Enum.GetValues<DayOfWeek>();

    public static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
