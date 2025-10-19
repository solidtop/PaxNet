using System.Net;
using System.Net.Sockets;

namespace PaxNet;

internal abstract record NetEvent;

internal sealed record ConnectionRequestEvent(ConnectionRequest Request) : NetEvent;

internal sealed record ConnectEvent(Connection Connection) : NetEvent;

internal sealed record DisconnectEvent(Connection Connection, DisconnectInfo Info) : NetEvent;

internal sealed record ReceiveEvent(Connection Connection, Packet Packet) : NetEvent;

internal sealed record RttEvent(Connection Connection, TimeSpan Rtt) : NetEvent;

internal sealed record ErrorEvent(IPEndPoint RemoteEndPoint, SocketError Error) : NetEvent;

internal static class NetEvents
{
    public static ConnectionRequestEvent ConnectionRequest(ConnectionRequest request)
    {
        return new ConnectionRequestEvent(request);
    }

    public static ConnectEvent Connect(Connection connection)
    {
        return new ConnectEvent(connection);
    }

    public static DisconnectEvent Disconnect(Connection connection, DisconnectInfo info)
    {
        return new DisconnectEvent(connection, info);
    }

    public static ReceiveEvent Receive(Connection connection, Packet packet)
    {
        return new ReceiveEvent(connection, packet);
    }

    public static RttEvent Rtt(Connection connection, TimeSpan rtt)
    {
        return new RttEvent(connection, rtt);
    }

    public static ErrorEvent Error(IPEndPoint remoteEndPoint, SocketError error)
    {
        return new ErrorEvent(remoteEndPoint, error);
    }
}