using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IModbusRequestHandler
{
    Task<ModbusResponse> HandleAsync(ModbusRequest request, CancellationToken ct);
}
