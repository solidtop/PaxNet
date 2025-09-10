using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket.Demo;

public class NetEventListener : INetEventListener
{
    public void OnClientConnected(Client client)
    {
        Console.WriteLine($"Client {client} connected");
    }

    public void OnClientDisconnected(Client client)
    {
        Console.WriteLine($"Client {client} disconnected");
    }

    public void OnPacketReceived(Client client, PacketReader reader, DeliveryMethod deliveryMethod)
    {
        Console.WriteLine($"Packet received from {client} with method {deliveryMethod}");

        var str = reader.ReadString();
        Console.WriteLine($"str: {str}");
    }

    public void OnError(Client client, SocketError error)
    {
        throw new NotImplementedException();
    }
}