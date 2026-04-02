using PlasticMes.ModbusSimulator.Application;

namespace PlasticMes.ModbusSimulator.Adapters.Tcp;

public sealed class ConnectionSessionStore : IConnectionSessionStore
{
    private readonly HashSet<long> sessions = [];
    private readonly Lock gate = new();

    public void Open(long id)
    {
        lock (gate)
        {
            sessions.Add(id);
        }
    }

    public void Close(long id)
    {
        lock (gate)
        {
            sessions.Remove(id);
        }
    }

    public int Count()
    {
        lock (gate)
        {
            return sessions.Count;
        }
    }
}
