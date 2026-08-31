namespace SlaWatcher;

/// <summary>
/// Measures how much of an interval falls inside the reviewer's working hours.
///
/// <para>
/// This is the only real arithmetic in the service, and the one place a wrong answer is
/// invisible: a figure that is off by an hour looks exactly like a figure that is right.
/// </para>
/// <para>
/// Not a Quartz calendar. <c>ICalendar</c> answers <c>IsTimeIncluded</c> and
/// <c>GetNextIncludedTimeUtc</c> and nothing else; both are questions about one instant. It
/// can stop a trigger firing out of hours, which is a different job from measuring.
/// </para>
/// </summary>
public sealed class WorkingHours
{
    /// <summary>
    /// A local time inside a spring-forward gap never appears on the wall clock. Walking
    /// forward a minute at a time finds the instant the clock reaches the boundary, and the
    /// bound is what stops a malformed zone turning that walk into a hang.
    /// </summary>
    private const int GapWalkLimitMinutes = 24 * 60;

    private readonly WorkingCalendar _calendar;

    public WorkingHours(WorkingCalendar calendar)
    {
        _calendar = calendar.Validated();
    }

    /// <summary>
    /// Working time between two instants.
    ///
    /// <para>
    /// Zero when the interval is empty or reversed, and that falls out of the arithmetic
    /// rather than being special-cased: a reversed interval either produces no dates to walk
    /// or an overlap whose end precedes its start, and neither contributes.
    /// </para>
    /// </summary>
    public TimeSpan Between(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var zone = _calendar.TimeZone;
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromUtc, zone).DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toUtc, zone).DateTime);

        var total = TimeSpan.Zero;

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!_calendar.IsWorkingDate(date))
            {
                continue;
            }

            // Resolved per date, never once for the whole interval. An interval that spans a
            // daylight-saving change has two different offsets in it, and reusing the first
            // one shifts every later window by an hour.
            var dayStart = StartOfWorking(date);
            var dayEnd = EndOfWorking(date);

            var overlapStart = dayStart > fromUtc ? dayStart : fromUtc;
            var overlapEnd = dayEnd < toUtc ? dayEnd : toUtc;

            if (overlapEnd > overlapStart)
            {
                total += overlapEnd - overlapStart;
            }
        }

        return total;
    }

    /// <summary>
    /// The instant the working day opens: the first time the wall clock reads
    /// <see cref="WorkingCalendar.DayStart" /> on this date.
    /// </summary>
    private DateTimeOffset StartOfWorking(DateOnly date) =>
        ToInstant(date.ToDateTime(_calendar.DayStart), preferEarliest: true);

    /// <summary>
    /// The instant the working day closes: the last time the wall clock reads
    /// <see cref="WorkingCalendar.DayEnd" /> on this date. Opening at the earliest reading and
    /// closing at the latest means a repeated hour is worked rather than skipped, which is
    /// what the person sitting there experienced.
    /// </summary>
    private DateTimeOffset EndOfWorking(DateOnly date) =>
        ToInstant(date.ToDateTime(_calendar.DayEnd), preferEarliest: false);

    private DateTimeOffset ToInstant(DateTime local, bool preferEarliest)
    {
        var zone = _calendar.TimeZone;

        if (zone.IsAmbiguousTime(local))
        {
            // The clock read this time twice. A larger offset is the earlier instant, because
            // the same wall clock further ahead of UTC happened sooner.
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var chosen = preferEarliest ? offsets.Max() : offsets.Min();
            return new DateTimeOffset(local, chosen);
        }

        if (zone.IsInvalidTime(local))
        {
            for (var minute = 1; minute <= GapWalkLimitMinutes; minute++)
            {
                var candidate = local.AddMinutes(minute);
                if (!zone.IsInvalidTime(candidate))
                {
                    return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
                }
            }

            throw new InvalidOperationException(
                $"No valid local time within a day of {local:O} in {zone.Id}.");
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
