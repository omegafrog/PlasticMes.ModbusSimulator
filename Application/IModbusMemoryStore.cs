using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public interface IModbusMemoryStore
{
    ModbusReadResult Read(ModbusRange range);

    ModbusWriteResult WriteClient(ModbusWrite write);

    ModbusWriteResult WriteScenario(ModbusWrite write);
}
