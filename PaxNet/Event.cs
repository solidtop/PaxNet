using System.Net;
using System.Net.Sockets;

namespace PaxNet;

internal abstract record Event;

internal record ConnectionRequestEvent(ConnectionRequest Request) : Event;

internal record ConnectEvent(Connection Connection) : Event;

internal record DisconnectEvent(Connection Connection, DisconnectReason Reason) : Event;

internal record ReceiveEvent(Connection Connection, Packet Packet, DeliveryMethod DeliveryMethod) : Event;

internal record RttEvent(Connection Connection, TimeSpan Rtt) : Event;

internal record ErrorEvent(IPEndPoint RemoteEndPoint, SocketError Error) : Event;

internal static class Events
{
    public static ConnectionRequestEvent ConnectionRequest(ConnectionRequest request)
    {
        return new ConnectionRequestEvent(request);
    }

    public static ConnectEvent Connect(Connection connection)
    {
        return new ConnectEvent(connection);
    }

    public static DisconnectEvent Disconnect(Connection connection, DisconnectReason reason)
    {
        return new DisconnectEvent(connection, reason);
    }

    public static ReceiveEvent Receive(Connection connection, Packet packet, DeliveryMethod deliveryMethod)
    {
        return new ReceiveEvent(connection, packet, deliveryMethod);
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