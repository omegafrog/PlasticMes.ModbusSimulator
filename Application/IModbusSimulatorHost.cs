using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IModbusSimulatorHost
{
    Task StartAsync(ModbusSimulatorHostConfig config, CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    SimulatorHealthSnapshot GetHealth();
}
