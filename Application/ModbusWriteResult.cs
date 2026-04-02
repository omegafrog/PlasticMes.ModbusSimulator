using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public sealed record ModbusWriteResult(bool IsSuccess, ModbusProtocolError? Error)
{
    public static ModbusWriteResult Success() => new(true, null);

    public static ModbusWriteResult Failure(ModbusProtocolError error) => new(false, error);
}
