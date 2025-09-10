using System.Net.Sockets;

namespace LogicalPacket.Core;

public interface INetEventListener
{
    void OnClientConnected(Client client);
    void OnClientDisconnected(Client client);
    void OnPacketReceived(Client client, PacketReader reader, DeliveryMethod deliveryMethod);
    void OnError(Client client, SocketError error);
}