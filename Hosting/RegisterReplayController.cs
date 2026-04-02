using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Hosting;

public sealed class RegisterReplayController : IRegisterReplayController
{
    private readonly IModbusMemoryStore memoryStore;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Lock gate = new();

    private CancellationTokenSource? runSource;
    private Task? runTask;
    private ReplayStatusSnapshot status = new(false, 0, 0, null, null);

    public RegisterReplayController(
        IModbusMemoryStore memoryStore,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.memoryStore = memoryStore;
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public Task StartAsync(RegisterReplayScenario scenario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ct.ThrowIfCancellationRequested();

        CancellationTokenSource source;
        lock (gate)
        {
            if (runTask is not null)
            {
                throw new InvalidOperationException("Replay is already running.");
            }

            source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            status = new ReplayStatusSnapshot(true, 0, scenario.Steps.Count, null, null);
            runSource = source;
            runTask = ExecuteAsync(scenario, source);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? source;
        Task? task;

        lock (gate)
        {
            source = runSource;
            task = runTask;
        }

        if (source is null || task is null)
        {
            return;
        }

        source.Cancel();
        try
        {
            await task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested && !ct.IsCancellationRequested)
        {
        }
        finally
        {
            source.Dispose();
        }
    }

    public ReplayStatusSnapshot GetStatus()
    {
        lock (gate)
        {
            return status;
        }
    }

    private async Task ExecuteAsync(RegisterReplayScenario scenario, CancellationTokenSource source)
    {
        var appliedStepCount = 0;
        int? currentRowNumber = null;

        try
        {
            TimeSpan previousOffset = default;
            foreach (var step in scenario.Steps)
            {
                var delay = step.Offset - previousOffset;
                if (delay > TimeSpan.Zero)
                {
                    await delayAsync(delay, source.Token);
                }

                foreach (var write in step.Writes)
                {
                    var result = memoryStore.WriteScenario(write);
                    if (!result.IsSuccess)
                    {
                        throw new InvalidOperationException(result.Error?.Message ?? "Scenario write failed.");
                    }
                }

                appliedStepCount++;
                currentRowNumber = step.RowNumber;
                previousOffset = step.Offset;
                lock (gate)
                {
                    status = new ReplayStatusSnapshot(true, appliedStepCount, scenario.Steps.Count, currentRowNumber, null);
                }
            }

            Complete(source, new ReplayStatusSnapshot(false, appliedStepCount, scenario.Steps.Count, currentRowNumber, null));
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            Complete(source, new ReplayStatusSnapshot(false, appliedStepCount, scenario.Steps.Count, currentRowNumber, null));
        }
        catch (Exception ex)
        {
            Complete(source, new ReplayStatusSnapshot(false, appliedStepCount, scenario.Steps.Count, currentRowNumber, ex.Message));
        }
    }

    private void Complete(CancellationTokenSource source, ReplayStatusSnapshot completedStatus)
    {
        lock (gate)
        {
            if (!ReferenceEquals(runSource, source))
            {
                return;
            }

            status = completedStatus;
            runSource = null;
            runTask = null;
        }
    }
}
