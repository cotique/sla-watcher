using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi.MongoDbJobStore;
using Quartz.Spi.MongoDbJobStore.Util;
using SlaWatcher;

var builder = Host.CreateApplicationBuilder(args);

// Read and validated before anything below takes a value out of it. The Quartz properties
// are plain strings assembled here, at build time, so this is the last point at which a
// missing setting can still be reported as a missing setting.
var options = SchedulerOptions.ReadAndValidate(builder.Configuration);

// The same instance the job and the installer resolve. One object, one validation, and no
// second read of the raw section to drift away from it.
builder.Services.AddSingleton(Options.Create(options));

builder.Services.AddQuartz(q =>
{
    // The job store is configured by property, not by a fluent extension: Quartz constructs
    // it itself from the type name. See the package README.
    q.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, options.InstanceName);

    // AUTO does not survive here: with the store reporting Clustered = false, Quartz writes
    // NON_CLUSTERED and both processes land on one scheduler row. Set it per process.
    q.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, options.InstanceId);

    q.SetProperty(StdSchedulerFactory.PropertyJobStoreType,
        typeof(MongoDbJobStore).AssemblyQualifiedName!);

    // Quartz refuses to start any non-RAM store without a serializer, and says so only at
    // startup. The package depends on Quartz.Serialization.SystemTextJson, which makes it
    // look already wired; it is not. UseSystemTextJsonSerializer() is an extension on
    // PersistentStoreOptions, which this store does not go through, so name the type.
    // The constant is "quartz.serializer" but the key Quartz reads is "quartz.serializer.type".
    // Passing PropertyObjectSerializer alone sets a key nothing looks at, and the failure is
    // the same message as setting nothing at all.
    q.SetProperty($"{StdSchedulerFactory.PropertyObjectSerializer}.type",
        typeof(SystemTextJsonObjectSerializer).AssemblyQualifiedName!);

    q.SetProperty(
        $"{StdSchedulerFactory.PropertyJobStorePrefix}.{StdSchedulerFactory.PropertyDataSourceConnectionString}",
        options.MongoConnectionString);

    q.SetProperty($"{StdSchedulerFactory.PropertyJobStorePrefix}.collectionPrefix",
        options.CollectionPrefix);

    // No "clustered" property here: the store has no setter for it and Quartz throws at
    // startup if you try. It reports Clustered = false and takes a distributed lock in every
    // method anyway, so two processes against one database already contend. The flag is
    // misleading; the behaviour is not.

    // The schedule is not declared here. Quartz's declarative initialisation checks whether
    // each job exists and then stores the ones that did not, and two instances starting
    // together on an empty database both pass the check and the second is refused. It is
    // installed after the scheduler is up instead, by ScheduleInstaller.
    q.AddJob<TickJob>(new JobKey("tick"), j => j.StoreDurably());
});

builder.Services.AddSingleton(new FireLog(options.MongoConnectionString));

// The watchdog runs on its own timer rather than as a job, so it still reports when the
// scheduler is the thing that is stuck.
builder.Services.AddSingleton(new StuckExecutionProbe(
    options.MongoConnectionString, options.CollectionPrefix, options.InstanceName));
builder.Services.AddHostedService<StuckExecutionMonitor>();

builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

// After AddQuartzHostedService on purpose: hosted services start in registration order, and
// this one needs a running scheduler.
builder.Services.AddHostedService<ScheduleInstaller>();

var host = builder.Build();

// Quartz builds the job store itself, so there is nowhere to inject a logger factory.
// Without this line the store runs silently and a connection problem looks like nothing
// happening at all. Assigned before the scheduler starts, as the package README requires.
JobStoreLogging.LoggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

if (options.RunSeconds > 0)
{
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    _ = Task.Delay(TimeSpan.FromSeconds(options.RunSeconds)).ContinueWith(_ => lifetime.StopApplication());
}

host.Run();
