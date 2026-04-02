using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IReplayScenarioLoader
{
    Task<ReplayScenarioLoadResult> LoadAsync(string csvPath, CancellationToken ct);
}
