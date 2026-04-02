namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ModbusRequest(
    ushort TransactionId,
    byte UnitId,
    byte FunctionCode,
    ModbusRange Range,
    ModbusWrite? Write = null);
