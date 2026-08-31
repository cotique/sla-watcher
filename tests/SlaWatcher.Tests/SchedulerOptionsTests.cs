namespace SlaWatcher.Tests;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

/// <summary>
/// The guard here is a decision, not a detail: appsettings.json carries no connection string
/// on purpose, so a run outside Development has to stop at boot instead of connecting to
/// whatever answers on the default port.
///
/// These call <see cref="SchedulerOptions.ReadAndValidate" /> because that is what Program.cs
/// calls. An earlier version of this file drove a ServiceCollection with
/// ValidateDataAnnotations instead, and passed while the application read the section
/// separately and never went through it. A test over a path the code does not take is worth
/// less than no test, because it also reports that the path is covered.
/// </summary>
public class SchedulerOptionsTests
{
    [Fact]
    public void AMissingConnectionStringFailsAndNamesTheSetting()
    {
        var failure = Assert.Throws<ValidationException>(() => Read(new Dictionary<string, string?>
        {
            ["Scheduler:InstanceName"] = "sla-watcher",
        }));

        // The message has to carry the setting. The failure this replaced said
        // "The connection string '' is not valid" and named the driver instead.
        Assert.Contains(nameof(SchedulerOptions.MongoConnectionString), failure.Message);
    }

    [Fact]
    public void AnEmptyConnectionStringFails()
    {
        // Distinct from missing: configuration providers hand back empty strings for keys that
        // are present and blank, and Required alone accepts those.
        Assert.Throws<ValidationException>(() => Read(new Dictionary<string, string?>
        {
            ["Scheduler:MongoConnectionString"] = string.Empty,
        }));
    }

    [Fact]
    public void AnAbsentSectionFailsRatherThanBindingDefaults()
    {
        // The section missing entirely binds to null, and the fallback instance carries every
        // default except the one that has none. It must not slip through as "configured".
        Assert.Throws<ValidationException>(() => Read(new Dictionary<string, string?>()));
    }

    [Fact]
    public void AConnectionStringIsEnough()
    {
        var options = Read(new Dictionary<string, string?>
        {
            ["Scheduler:MongoConnectionString"] = "mongodb://localhost:27117/sla-watcher",
        });

        Assert.Equal("mongodb://localhost:27117/sla-watcher", options.MongoConnectionString);

        // The cron floor is a documented decision, so the default carries it rather than
        // leaving each deployment to rediscover that anything under a minute is silently eaten.
        Assert.Equal("0 * * * * ?", options.TickCron);
    }

    private static SchedulerOptions Read(Dictionary<string, string?> settings) =>
        SchedulerOptions.ReadAndValidate(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
}
