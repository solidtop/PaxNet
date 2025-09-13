using System.Net;

namespace LogicalPacket.Core;

public class ConnectionRequest
{
    private readonly Server _server;
    private readonly IPEndPoint _remoteEndPoint;
    private readonly Packet _packet;

    internal ConnectionRequest(Server server, IPEndPoint remoteEndPoint, Packet packet)
    {
        _server = server;
        _remoteEndPoint = remoteEndPoint;
        _packet = packet;
    }

    public void Accept()
    {
        _server.AcceptConnection(_remoteEndPoint);
    }

    public void AcceptIfKey(string key)
    {
        var reader = new PacketReader(_packet.Payload.Span);

        try
        {
            if (reader.ReadString() == key)
                _server.AcceptConnection(_remoteEndPoint);
            else
                _server.RejectConnection(_remoteEndPoint);
        }
        catch
        {
            Console.WriteLine("Invalid incoming data");
        }
    }

    public void Reject()
    {
        _server.RejectConnection(_remoteEndPoint);
    }
}