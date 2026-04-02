namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ReplayScenarioLoadResult(
    bool IsSuccess,
    RegisterReplayScenario? Scenario,
    string? ErrorMessage)
{
    public static ReplayScenarioLoadResult Success(RegisterReplayScenario scenario) => new(true, scenario, null);

    public static ReplayScenarioLoadResult Failure(string errorMessage) => new(false, null, errorMessage);
}
