using System.Net;

namespace PlasticMes.ModbusSimulator.Contracts;

public sealed record ModbusSimulatorHostConfig(
    IPAddress BindAddress,
    int Port,
    byte UnitId);
