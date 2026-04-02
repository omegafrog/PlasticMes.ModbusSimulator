namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ModbusWrite(
    ModbusRange Range,
    IReadOnlyList<ushort>? RegisterValues = null,
    IReadOnlyList<bool>? BitValues = null);
