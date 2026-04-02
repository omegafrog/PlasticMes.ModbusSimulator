namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ModbusResponse(
    ushort TransactionId,
    byte UnitId,
    byte FunctionCode,
    ReadOnlyMemory<byte> Data,
    byte? ExceptionCode = null,
    ModbusProtocolError? Error = null);
