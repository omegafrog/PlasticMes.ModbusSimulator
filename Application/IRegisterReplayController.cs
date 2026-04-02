using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IRegisterReplayController
{
    Task StartAsync(RegisterReplayScenario scenario, CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    ReplayStatusSnapshot GetStatus();
}
