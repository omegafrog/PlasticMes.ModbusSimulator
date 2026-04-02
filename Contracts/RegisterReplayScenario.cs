namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record RegisterReplayScenario(
    string SourcePath,
    IReadOnlyList<ReplayColumn> Columns,
    IReadOnlyList<ReplayStep> Steps);
