namespace PaxNet;

internal abstract record NetEvent;

internal sealed record ConnectionRequestEvent(ConnectionRequest Request) : NetEvent;

internal sealed record ConnectEvent(Connection Connection) : NetEvent;

internal sealed record DisconnectEvent(Connection Connection, DisconnectInfo Info) : NetEvent;

internal sealed record RttEvent(Connection Connection, TimeSpan Rtt) : NetEvent;

internal static class NetEvents
{
    public static NetEvent ConnectionRequest(ConnectionRequest request)
    {
        return new ConnectionRequestEvent(request);
    }

    public static NetEvent Connect(Connection connection)
    {
        return new ConnectEvent(connection);
    }

    public static NetEvent Disconnect(Connection connection, DisconnectInfo info)
    {
        return new DisconnectEvent(connection, info);
    }

    public static NetEvent Rtt(Connection connection, TimeSpan rtt)
    {
        return new RttEvent(connection, rtt);
    }
}