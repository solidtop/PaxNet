namespace PaxNet;

public class ConnectionRequest
{
    private readonly Connection _connection;
    private readonly Packet _packet;
    private bool _resolved;

    internal ConnectionRequest(Connection connection, Packet packet)
    {
        _connection = connection;
        _packet = packet;
    }

    public void Accept()
    {
        if (_resolved) return;
        _resolved = true;
        _connection.Accept();
        _packet.Dispose();

        using var acceptPacket = Packet.Create(PacketType.ConnectionAccept);
        _connection.SendCore(acceptPacket.Data);
    }

    public void Reject()
    {
        if (_resolved) return;
        _resolved = true;
        _connection.Reject();
        _packet.Dispose();

        using var rejectPacket = Packet.Create(PacketType.ConnectionReject);
        _connection.SendCore(rejectPacket.Data);
    }

    public void AcceptIfKey(string key)
    {
        if (_resolved) return;

        try
        {
            if (_packet.Reader.ReadString() == key)
                Accept();
            else
                Reject();
        }
        catch
        {
            Console.WriteLine("Invalid data in connection request");
            Reject();
        }
    }
}