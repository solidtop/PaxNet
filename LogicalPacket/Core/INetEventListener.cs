using System.Net;
using System.Net.Sockets;

namespace LogicalPacket.Core;

public interface INetEventListener
{
    void OnPeerConnected(Peer peer);
    void OnPeerDisconnected(Peer peer);
    void OnPacketReceived(Peer peer, PacketReader reader, DeliveryMethod deliveryMethod);
    void OnError(IPEndPoint endPoint, SocketError error);
}