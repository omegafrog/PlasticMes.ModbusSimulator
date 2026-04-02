namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record SimulatorHealthSnapshot(
    SimulatorHostState State,
    int Port,
    int SessionCount,
    DateTimeOffset? StartedAt,
    string? LastError = null);
