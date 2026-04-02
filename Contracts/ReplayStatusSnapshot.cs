namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ReplayStatusSnapshot(
    bool IsRunning,
    int AppliedStepCount,
    int TotalStepCount,
    int? CurrentRowNumber,
    string? LastError);
