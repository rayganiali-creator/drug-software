using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedSmarter.BuildingBlocks;

/// <summary>
/// The smallest background-processing abstraction: run one bounded pass every <c>interval</c> inside the API process until the host stops.
/// No external queue and no extra service. A pass that throws is logged by name only (never with data) and the loop continues; shutdown cancels the
/// current pass through the token and the loop ends. Everything a pass does must be idempotent, because a pass can be cut off at any point.
/// </summary>
public sealed class PeriodicWorker(string name, TimeSpan interval, Func<CancellationToken, Task> pass, ILogger logger, TimeSpan? initialDelay = null) : BackgroundService
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, string, string, Exception?> PassFailed =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(7001, "BackgroundPassFailed"), "Background pass {Worker} failed ({ErrorType}); it will be retried at the next interval.");

    public string Name => name;

    public TimeSpan Interval { get; } = interval < MinimumInterval ? MinimumInterval : interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(initialDelay ?? TimeSpan.FromSeconds(2), stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    /// <summary>One pass. Exposed so tests can drive the worker deterministically without timers.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await pass(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one failing pass must not stop the worker; the failure is recorded by name only, never with patient data
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PassFailed(logger, name, ex.GetType().Name, null);
        }
    }
}
