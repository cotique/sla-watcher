namespace SlaWatcher.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// The guard these cover is a decision, not a detail: appsettings.json carries no connection
/// string on purpose, so a run outside Development has to stop at boot instead of connecting
/// to whatever answers on the default port.
///
/// Wired through the real options pipeline rather than calling the validator directly. What
/// has to hold is that the application refuses to start, and that only shows up if the
/// binding, the annotations and ValidateDataAnnotations are all in the same place they are in
/// Program.cs.
/// </summary>
public class SchedulerOptionsTests
{
    [Fact]
    public void AMissingConnectionStringFailsValidation()
    {
        var options = Build(new Dictionary<string, string?>
        {
            ["Scheduler:InstanceName"] = "sla-watcher",
        });

        var failure = Assert.Throws<OptionsValidationException>(() => _ = options.Value);
        Assert.Contains(nameof(SchedulerOptions.MongoConnectionString), failure.Message);
    }

    [Fact]
    public void AnEmptyConnectionStringFailsValidation()
    {
        // Distinct from missing: configuration providers hand back empty strings for keys that
        // are present and blank, and Required alone accepts those.
        var options = Build(new Dictionary<string, string?>
        {
            ["Scheduler:MongoConnectionString"] = string.Empty,
        });

        Assert.Throws<OptionsValidationException>(() => _ = options.Value);
    }

    [Fact]
    public void AConnectionStringIsEnough()
    {
        var options = Build(new Dictionary<string, string?>
        {
            ["Scheduler:MongoConnectionString"] = "mongodb://localhost:27117/sla-watcher",
        });

        Assert.Equal("mongodb://localhost:27117/sla-watcher", options.Value.MongoConnectionString);

        // The cron floor is a documented decision, so the default carries it rather than
        // leaving each deployment to rediscover that anything under a minute is silently eaten.
        Assert.Equal("0 * * * * ?", options.Value.TickCron);
    }

    private static IOptions<SchedulerOptions> Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddOptions<SchedulerOptions>()
            .Bind(configuration.GetSection(SchedulerOptions.SectionName))
            .ValidateDataAnnotations();

        return services.BuildServiceProvider().GetRequiredService<IOptions<SchedulerOptions>>();
    }
}
