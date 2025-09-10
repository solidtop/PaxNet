using System.Net.Sockets;

namespace LogicalPacket.Core;

internal abstract record NetEvent(Client Client);

internal sealed record ConnectEvent(Client Client) : NetEvent(Client);

internal sealed record DisconnectEvent(Client Client) : NetEvent(Client);

internal sealed record ReceiveEvent(Client Client, Packet Packet, DeliveryMethod DeliveryMethod) : NetEvent(Client);

internal sealed record ErrorEvent(Client Client, SocketError Error) : NetEvent(Client);

internal static class NetEvents
{
    public static ConnectEvent Connect(Client client)
    {
        return new ConnectEvent(client);
    }

    public static DisconnectEvent Disconnect(Client client)
    {
        return new DisconnectEvent(client);
    }

    public static ReceiveEvent Receive(Client client, Packet packet, DeliveryMethod deliveryMethod)
    {
        return new ReceiveEvent(client, packet, deliveryMethod);
    }

    public static ErrorEvent Error(Client client, SocketError error)
    {
        return new ErrorEvent(client, error);
    }
}

public enum DeliveryMethod
{
    Unreliable,
    Reliable,
    Sequenced
}