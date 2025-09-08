using System.Net;

namespace LogicalPacket.Core;

public sealed class Client(Server server, IPEndPoint remoteEndPoint)
    : IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port)
{
    private readonly Server _server = server;

    private readonly Packet _pingPacket = new(PacketType.Ping);
    private readonly Packet _pongPacket = new(PacketType.Pong);

    internal void ProcessPacket(Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.Ping:
                _server.Send(_pongPacket, this);
                packet.Dispose();
                break;
        }
    }
}