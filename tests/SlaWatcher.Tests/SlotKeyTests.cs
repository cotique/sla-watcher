namespace SlaWatcher.Tests;
using System.Globalization;

/// <summary>
/// The key is what makes a retry collide instead of adding a second row, so what is worth
/// testing is not that it produces a string — it is that two attempts at one slot cannot
/// disagree about it, whatever machine they run on.
/// </summary>
public class SlotKeyTests
{
    private const string Trigger = "DEFAULT.tick-trigger";

    [Fact]
    public void SameInstantInDifferentOffsetsKeysTheSame()
    {
        // One instant, written as two local times. A pod in Warsaw and a pod in UTC are given
        // the same slot by Quartz and have to agree on its key.
        var warsaw = new DateTimeOffset(2026, 8, 23, 21, 35, 0, TimeSpan.FromHours(2));
        var utc = new DateTimeOffset(2026, 8, 23, 19, 35, 0, TimeSpan.Zero);

        Assert.Equal(FireLog.SlotKey(Trigger, utc), FireLog.SlotKey(Trigger, warsaw));
    }

    [Fact]
    public void KeyDoesNotDependOnTheCurrentCulture()
    {
        var slot = new DateTimeOffset(2026, 8, 23, 19, 35, 0, TimeSpan.Zero);

        var invariant = WithCulture(CultureInfo.InvariantCulture, () => FireLog.SlotKey(Trigger, slot));

        // A calendar that does not number years the same way. A culture-sensitive format
        // would produce a different string here and the collision would stop happening.
        var umAlQura = WithCulture(new CultureInfo("ar-SA"), () => FireLog.SlotKey(Trigger, slot));

        Assert.Equal(invariant, umAlQura);
    }

    [Fact]
    public void DifferentSlotsKeyDifferently()
    {
        var first = new DateTimeOffset(2026, 8, 23, 19, 35, 0, TimeSpan.Zero);
        var second = first.AddMinutes(1);

        Assert.NotEqual(FireLog.SlotKey(Trigger, first), FireLog.SlotKey(Trigger, second));
    }

    [Fact]
    public void AnUnscheduledFireKeysOnTheTriggerAlone()
    {
        Assert.Equal($"{Trigger}:unscheduled", FireLog.SlotKey(Trigger, null));
    }

    private static string WithCulture(CultureInfo culture, Func<string> body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
