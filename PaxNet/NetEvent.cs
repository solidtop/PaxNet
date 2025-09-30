using System.Net;
using System.Net.Sockets;

namespace PaxNet.Core;

internal abstract record NetEvent;

internal sealed record ConnectionRequestEvent(ConnectionRequest Request) : NetEvent;

internal sealed record ConnectEvent(Peer Peer) : NetEvent;

internal sealed record DisconnectEvent(Peer Peer, DisconnectInfo Info) : NetEvent;

internal sealed record ReceiveEvent(Peer Peer, Packet Packet, DeliveryMethod DeliveryMethod) : NetEvent;

internal sealed record RttEvent(Peer Peer, TimeSpan Rtt) : NetEvent;

internal sealed record ErrorEvent(IPEndPoint RemoteEndPoint, SocketError Error) : NetEvent;

internal static class NetEvents
{
    public static ConnectionRequestEvent ConnectionRequest(ConnectionRequest request)
    {
        return new ConnectionRequestEvent(request);
    }

    public static ConnectEvent Connect(Peer peer)
    {
        return new ConnectEvent(peer);
    }

    public static DisconnectEvent Disconnect(Peer peer, DisconnectInfo info)
    {
        return new DisconnectEvent(peer, info);
    }

    public static ReceiveEvent Receive(Peer peer, Packet packet, DeliveryMethod deliveryMethod)
    {
        return new ReceiveEvent(peer, packet, deliveryMethod);
    }
    
    public static RttEvent Rtt(Peer peer, TimeSpan rtt)
    {
        return new RttEvent(peer, rtt);
    }

    public static ErrorEvent Error(IPEndPoint remoteEndPoint, SocketError error)
    {
        return new ErrorEvent(remoteEndPoint, error);
    }
}

public enum DeliveryMethod
{
    Unreliable,
    Reliable,
    Sequenced
}

public enum DisconnectReason
{
    Unknown,
    ServerShutdown,
    Timeout,
    ConnectionLost,
    ConnectionRefused,
    RemoteClose,
    LocalClose
}

public record struct DisconnectInfo(DisconnectReason Reason, SocketError? SocketError);