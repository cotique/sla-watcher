namespace SlaWatcher;

/// <summary>
/// The reviewer's working week: which days count, which hours of them, and which dates are
/// off regardless.
///
/// <para>
/// <see cref="Version" /> is stored next to every number this calendar produces. A month's
/// figure is only readable a year later if the definition that produced it can be named, and
/// a calendar that gains a holiday is a different definition.
/// </para>
/// </summary>
public sealed record WorkingCalendar(
    TimeZoneInfo TimeZone,
    TimeOnly DayStart,
    TimeOnly DayEnd,
    IReadOnlySet<DayOfWeek> WorkingDays,
    IReadOnlySet<DateOnly> Holidays,
    string Version)
{
    /// <summary>
    /// Rejects a calendar that cannot be measured against, at construction rather than at the
    /// first odd number in a report.
    /// </summary>
    public WorkingCalendar Validated()
    {
        if (DayEnd <= DayStart)
        {
            throw new ArgumentException(
                $"The working day ends at {DayEnd} and starts at {DayStart}. A day that wraps " +
                "past midnight is not supported: every interval here is measured within one " +
                "local date.");
        }

        if (WorkingDays.Count == 0)
        {
            throw new ArgumentException("A calendar with no working days measures every interval as zero.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("The calendar version is what makes a stored number readable later.");
        }

        return this;
    }

    /// <summary>True when this local date contributes any working time at all.</summary>
    public bool IsWorkingDate(DateOnly date) =>
        WorkingDays.Contains(date.DayOfWeek) && !Holidays.Contains(date);
}
