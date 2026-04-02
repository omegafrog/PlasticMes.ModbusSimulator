using System.Net;
using System.Net.Sockets;
using PlasticMes.ModbusSimulator.Application;
using PlasticMes.ModbusSimulator.Contracts;

namespace PlasticMes.ModbusSimulator.Hosting;

public sealed class ModbusSimulatorHost(
    IModbusFrameCodec frameCodec,
    IModbusRequestHandler requestHandler,
    IConnectionSessionStore sessionStore) : IModbusSimulatorHost
{
    private readonly Lock gate = new();
    private TcpListener? listener;
    private CancellationTokenSource? stopSource;
    private Task? acceptLoop;
    private SimulatorHealthSnapshot health = new(SimulatorHostState.Stopped, 0, 0, null);
    private long sessionSeed;

    public async Task StartAsync(ModbusSimulatorHostConfig config, CancellationToken ct)
    {
        lock (gate)
        {
            if (listener is not null)
            {
                return;
            }

            listener = new TcpListener(config.BindAddress, config.Port);
            stopSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            health = new SimulatorHealthSnapshot(SimulatorHostState.Starting, config.Port, sessionStore.Count(), null);
        }

        try
        {
            listener.Start();
            var resolvedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            lock (gate)
            {
                health = new SimulatorHealthSnapshot(SimulatorHostState.Running, resolvedPort, sessionStore.Count(), DateTimeOffset.UtcNow);
                acceptLoop = AcceptLoopAsync(listener, stopSource!.Token);
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                health = new SimulatorHealthSnapshot(SimulatorHostState.Faulted, config.Port, sessionStore.Count(), null, ex.Message);
                listener = null;
                stopSource?.Dispose();
                stopSource = null;
            }

            throw;
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        TcpListener? localListener;
        CancellationTokenSource? localStopSource;
        Task? localAcceptLoop;

        lock (gate)
        {
            localListener = listener;
            localStopSource = stopSource;
            localAcceptLoop = acceptLoop;
            listener = null;
            stopSource = null;
            acceptLoop = null;
        }

        if (localListener is null || localStopSource is null)
        {
            return;
        }

        localStopSource.Cancel();
        localListener.Stop();
        if (localAcceptLoop is not null)
        {
            await localAcceptLoop.WaitAsync(ct);
        }

        localStopSource.Dispose();
        lock (gate)
        {
            health = new SimulatorHealthSnapshot(SimulatorHostState.Stopped, health.Port, sessionStore.Count(), health.StartedAt, health.LastError);
        }
    }

    public SimulatorHealthSnapshot GetHealth()
    {
        lock (gate)
        {
            return health with { SessionCount = sessionStore.Count() };
        }
    }

    private async Task AcceptLoopAsync(TcpListener tcpListener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await tcpListener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    health = health with { State = SimulatorHostState.Faulted, LastError = ex.Message, SessionCount = sessionStore.Count() };
                }

                client?.Dispose();
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stopToken)
    {
        var sessionId = Interlocked.Increment(ref sessionSeed);
        sessionStore.Open(sessionId);
        lock (gate)
        {
            health = health with { SessionCount = sessionStore.Count() };
        }

        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                while (!stopToken.IsCancellationRequested)
                {
                    var header = await ReadExactAsync(stream, 7, stopToken);
                    if (header is null)
                    {
                        break;
                    }

                    var length = ReadUInt16BigEndian(header, 4);
                    if (length < 2)
                    {
                        throw new IOException("Invalid MBAP length received from client.");
                    }

                    var body = await ReadExactAsync(stream, length - 1, stopToken);
                    if (body is null)
                    {
                        break;
                    }

                    var frame = new byte[header.Length + body.Length];
                    header.CopyTo(frame, 0);
                    body.CopyTo(frame, header.Length);

                    var decoded = frameCodec.Decode(frame);
                    var response = decoded.IsSuccess
                        ? await requestHandler.HandleAsync(decoded.Value!, stopToken)
                        : decoded.ErrorResponse!;
                    await stream.WriteAsync(frameCodec.Encode(response), stopToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException ex)
            {
                lock (gate)
                {
                    health = health with { LastError = ex.Message };
                }
            }
            catch (SocketException ex)
            {
                lock (gate)
                {
                    health = health with { LastError = ex.Message };
                }
            }
            finally
            {
                sessionStore.Close(sessionId);
                lock (gate)
                {
                    health = health with { SessionCount = sessionStore.Count() };
                }
            }
        }
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var read = 0;

        while (read < count)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (chunk == 0)
            {
                return read == 0 ? null : throw new IOException("Connection closed mid-frame.");
            }

            read += chunk;
        }

        return buffer;
    }

    private static ushort ReadUInt16BigEndian(IReadOnlyList<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}
