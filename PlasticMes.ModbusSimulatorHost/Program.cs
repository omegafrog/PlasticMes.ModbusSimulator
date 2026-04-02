using System.Net;
using PlasticMes.ModbusSimulator.Adapters.Csv;
using PlasticMes.ModbusSimulator.Adapters.Memory;
using PlasticMes.ModbusSimulator.Adapters.Tcp;
using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;
using PlasticMes.ModbusSimulator.Hosting;

var options = ParseArguments(args);

var memoryStore = new InMemoryModbusMemoryStore();
var frameCodec = new ModbusTcpFrameCodec();
var requestHandler = new ModbusRequestHandler(memoryStore);
var sessionStore = new ConnectionSessionStore();
var host = new ModbusSimulatorHost(frameCodec, requestHandler, sessionStore);
var replayLoader = new CsvReplayScenarioLoader();
var replayController = new RegisterReplayController(memoryStore);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

RegisterReplayScenario? replayScenario = null;
if (options.CsvPath is not null)
{
    var loadResult = await replayLoader.LoadAsync(options.CsvPath, shutdown.Token);
    if (!loadResult.IsSuccess)
    {
        Console.Error.WriteLine(loadResult.ErrorMessage);
        return 1;
    }

    replayScenario = loadResult.Scenario;
}

await host.StartAsync(new ModbusSimulatorHostConfig(options.BindAddress, options.Port, options.UnitId), shutdown.Token);

try
{
    if (replayScenario is not null)
    {
        await replayController.StartAsync(replayScenario, shutdown.Token);
    }

    var health = host.GetHealth();
    Console.WriteLine($"Modbus TCP simulator listening on {options.BindAddress}:{health.Port} (unit-id={options.UnitId}).");

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
    }
}
finally
{
    await replayController.StopAsync(CancellationToken.None);
    await host.StopAsync(CancellationToken.None);
}

return 0;

static HostOptions ParseArguments(IReadOnlyList<string> args)
{
    var bindAddress = IPAddress.Loopback;
    var port = 1502;
    byte unitId = 1;
    string? csvPath = null;

    for (var index = 0; index < args.Count; index++)
    {
        var arg = args[index];
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"Missing value for argument '{arg}'.");
        }

        var value = args[++index];
        switch (arg)
        {
            case "--bind":
                bindAddress = IPAddress.Parse(value);
                break;
            case "--port":
                port = int.Parse(value);
                break;
            case "--unit-id":
                unitId = byte.Parse(value);
                break;
            case "--csv":
                csvPath = value;
                break;
            default:
                throw new ArgumentException($"Unknown argument '{arg}'.");
        }
    }

    return new HostOptions(bindAddress, port, unitId, csvPath);
}

internal sealed record HostOptions(IPAddress BindAddress, int Port, byte UnitId, string? CsvPath);
