using System.Buffers;
using System.Net;
using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket.Demo;

public class NetEventListener : INetEventListener
{
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey("MyKey");
    }

    public void OnPeerConnected(Peer peer)
    {
        Console.WriteLine($"Peer {peer} connected");
    }

    public void OnPeerDisconnected(Peer peer, DisconnectInfo info)
    {
        Console.WriteLine($"Peer {peer} disconnected with reason: {info.Reason}");
    }

    public void OnPacketReceived(Peer peer, PacketReader reader, DeliveryMethod deliveryMethod)
    {
        Console.WriteLine($"Packet received from {peer} with method {deliveryMethod}");

        using var buffer = _bufferPool.Rent(1024);
        var writer = new PacketWriter(buffer.Memory.Span);
        writer.WriteString("Hello from server");
        peer.Send(writer.Data, DeliveryMethod.Unreliable);
    }

    public void OnError(IPEndPoint endPoint, SocketError error)
    {
        throw new NotImplementedException();
    }
}