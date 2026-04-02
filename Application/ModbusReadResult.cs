using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Application;

public sealed record ModbusReadResult(
    bool IsSuccess,
    IReadOnlyList<bool>? BitValues,
    IReadOnlyList<ushort>? RegisterValues,
    ModbusProtocolError? Error)
{
    public static ModbusReadResult SuccessBits(IReadOnlyList<bool> values) => new(true, values, null, null);

    public static ModbusReadResult SuccessRegisters(IReadOnlyList<ushort> values) => new(true, null, values, null);

    public static ModbusReadResult Failure(ModbusProtocolError error) => new(false, null, null, error);
}
