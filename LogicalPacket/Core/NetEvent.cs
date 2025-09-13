using System.Net.Sockets;

namespace LogicalPacket.Core;

internal abstract record NetEvent(Peer Peer);

internal sealed record ConnectEvent(Peer Peer) : NetEvent(Peer);

internal sealed record DisconnectEvent(Peer Peer, DisconnectInfo Info) : NetEvent(Peer);

internal sealed record ReceiveEvent(Peer Peer, Packet Packet, DeliveryMethod DeliveryMethod) : NetEvent(Peer);

internal sealed record ErrorEvent(Peer Peer, SocketError Error) : NetEvent(Peer);

internal static class NetEvents
{
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

    public static ErrorEvent Error(Peer peer, SocketError error)
    {
        return new ErrorEvent(peer, error);
    }
}

public enum DeliveryMethod
{
    Unreliable,
    Reliable,
    Sequenced
}