namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ReplayStep(TimeSpan Offset, IReadOnlyList<ModbusWrite> Writes, int RowNumber);
