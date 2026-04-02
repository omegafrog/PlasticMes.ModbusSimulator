namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ModbusProtocolError(ModbusExceptionCode ExceptionCode, string Message);
