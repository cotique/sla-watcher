namespace SlaWatcher.Tests;

/// <summary>
/// The arithmetic the whole metric rests on. Every case here is written as instants in UTC,
/// because a test whose inputs are local times cannot say what it expected across a
/// daylight-saving change.
///
/// A zone that changes offset twice a year, so the daylight-saving cases are real rather
/// than hypothetical.
/// </summary>
public class WorkingHoursTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static WorkingHours Standard(params DateOnly[] holidays) =>
        new(new WorkingCalendar(
            TimeZone: Warsaw,
            DayStart: new TimeOnly(9, 0),
            DayEnd: new TimeOnly(18, 0),
            WorkingDays: new HashSet<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday,
            },
            Holidays: holidays.ToHashSet(),
            Version: "test"));

    /// <summary>An instant given as Warsaw wall-clock time, resolved through the zone.</summary>
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Warsaw.GetUtcOffset(local));
    }

    [Fact]
    public void InsideOneWorkingDay()
    {
        // Wednesday.
        var hours = Standard().Between(Local(2026, 8, 12, 10), Local(2026, 8, 12, 14, 30));

        Assert.Equal(TimeSpan.FromHours(4.5), hours);
    }

    [Fact]
    public void ANightBetweenTwoWorkingDaysDoesNotCount()
    {
        // Wednesday 16:00 to Thursday 11:00: two hours left of Wednesday, two into Thursday.
        var hours = Standard().Between(Local(2026, 8, 12, 16), Local(2026, 8, 13, 11));

        Assert.Equal(TimeSpan.FromHours(4), hours);
    }

    [Fact]
    public void AWeekendDoesNotCount()
    {
        // Friday 16:00 to Monday 10:00: two hours of Friday, one of Monday.
        var hours = Standard().Between(Local(2026, 8, 14, 16), Local(2026, 8, 17, 10));

        Assert.Equal(TimeSpan.FromHours(3), hours);
    }

    [Fact]
    public void AHolidayDoesNotCount()
    {
        // Thursday 2026-08-13 declared off: Wednesday 16:00 to Friday 10:00 keeps two hours
        // of Wednesday and one of Friday, and loses the whole of Thursday.
        var calendar = Standard(new DateOnly(2026, 8, 13));

        var hours = calendar.Between(Local(2026, 8, 12, 16), Local(2026, 8, 14, 10));

        Assert.Equal(TimeSpan.FromHours(3), hours);
    }

    [Fact]
    public void TimeOutsideTheWorkingDayIsIgnoredAtBothEnds()
    {
        // 06:00 to 22:00 on a Wednesday is the whole working day and nothing else.
        var hours = Standard().Between(Local(2026, 8, 12, 6), Local(2026, 8, 12, 22));

        Assert.Equal(TimeSpan.FromHours(9), hours);
    }

    [Fact]
    public void AnIntervalEntirelyOutsideWorkingHoursIsZero()
    {
        // Saturday afternoon.
        var hours = Standard().Between(Local(2026, 8, 15, 12), Local(2026, 8, 15, 18));

        Assert.Equal(TimeSpan.Zero, hours);
    }

    [Fact]
    public void AnEmptyIntervalIsZero()
    {
        var at = Local(2026, 8, 12, 10);

        Assert.Equal(TimeSpan.Zero, Standard().Between(at, at));
    }

    [Fact]
    public void AReversedIntervalIsZeroRatherThanNegative()
    {
        // A ticket recorded as leaving a status before it entered must contribute nothing,
        // not subtract from the month.
        var hours = Standard().Between(Local(2026, 8, 12, 14), Local(2026, 8, 12, 10));

        Assert.Equal(TimeSpan.Zero, hours);
    }

    [Fact]
    public void AnIntervalSpanningTheSpringForwardKeepsItsWorkingHours()
    {
        // Warsaw moves +01:00 to +02:00 in the small hours of Sunday 2026-03-29.
        // Friday 16:00 to Monday 10:00: two hours of Friday, one of Monday. The skipped hour
        // fell on a Sunday and was never working time.
        //
        // This is the case that catches an implementation resolving the offset once for the
        // whole interval: Monday's window would be built with Friday's offset, an hour out,
        // and Monday would contribute nothing.
        var hours = Standard().Between(Local(2026, 3, 27, 16), Local(2026, 3, 30, 10));

        Assert.Equal(TimeSpan.FromHours(3), hours);
    }

    [Fact]
    public void AnIntervalSpanningTheAutumnChangeKeepsItsWorkingHours()
    {
        // Warsaw moves +02:00 back to +01:00 in the small hours of Sunday 2026-10-25.
        // Friday 16:00 to Monday 10:00, same shape as above, opposite direction.
        var hours = Standard().Between(Local(2026, 10, 23, 16), Local(2026, 10, 26, 10));

        Assert.Equal(TimeSpan.FromHours(3), hours);
    }

    [Fact]
    public void ARepeatedHourInsideTheWorkingDayIsWorked()
    {
        // The one case where it matters which of two identical clock readings is meant.
        // Warsaw puts 03:00 back to 02:00 on Sunday 2026-10-25, so the wall clock reads
        // 02:00 to 03:00 twice. A window of 02:00 to 04:00 on that date is three real hours,
        // because the person sitting there worked the repeated hour.
        //
        // Opening at the earliest reading of 02:00 and closing at the latest reading of 04:00
        // is what produces three. The other pairing gives two and looks equally plausible.
        var nightShift = new WorkingHours(new WorkingCalendar(
            TimeZone: Warsaw,
            DayStart: new TimeOnly(2, 0),
            DayEnd: new TimeOnly(4, 0),
            WorkingDays: new HashSet<DayOfWeek> { DayOfWeek.Sunday },
            Holidays: new HashSet<DateOnly>(),
            Version: "test-night"));

        var wholeDay = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var hours = nightShift.Between(wholeDay, wholeDay.AddDays(2));

        Assert.Equal(TimeSpan.FromHours(3), hours);
    }

    [Fact]
    public void AWholeWorkingWeekIsFortyFiveHours()
    {
        // Monday 00:00 to Saturday 00:00, five nine-hour days.
        var hours = Standard().Between(Local(2026, 8, 10, 0), Local(2026, 8, 15, 0));

        Assert.Equal(TimeSpan.FromHours(45), hours);
    }

    [Fact]
    public void ACalendarThatEndsBeforeItStartsIsRejected()
    {
        var wrapping = new WorkingCalendar(
            Warsaw, new TimeOnly(22, 0), new TimeOnly(6, 0),
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(), "test");

        Assert.Throws<ArgumentException>(() => new WorkingHours(wrapping));
    }

    [Fact]
    public void ACalendarWithoutAVersionIsRejected()
    {
        // The version is what makes a stored figure readable a year later, so a calendar
        // without one cannot be used to produce a stored figure.
        var unversioned = new WorkingCalendar(
            Warsaw, new TimeOnly(9, 0), new TimeOnly(18, 0),
            new HashSet<DayOfWeek> { DayOfWeek.Monday }, new HashSet<DateOnly>(), "  ");

        Assert.Throws<ArgumentException>(() => new WorkingHours(unversioned));
    }
}
