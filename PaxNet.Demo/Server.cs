using System.Net;
using System.Net.Sockets;

namespace PaxNet.Demo;

public class Server : IConnectionListener
{
    private readonly Host _host;

    public Server()
    {
        _host = new Host(this);
    }

    public void OnConnectionRequested(ConnectionRequest request)
    {
        request.AcceptIfKey("MyKey");
    }

    public void OnConnected(Connection connection)
    {
        Print($"{connection.RemoteEndPoint} connected");
    }

    public void OnDisconnected(Connection connection, DisconnectReason reason)
    {
        Print($"{connection.RemoteEndPoint} disconnected with reason {reason}");
    }

    public void OnDataReceived(Connection connection, PacketReader reader, DeliveryMethod deliveryMethod)
    {
    }

    public void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
        Print($"{connection.RemoteEndPoint} rtt: {rtt.Milliseconds} ms");
    }

    public void OnErrorOccured(IPEndPoint remoteEndPoint, SocketError error)
    {
        Print($"{remoteEndPoint} error: {error}");
    }

    public void Start(IPEndPoint endPoint)
    {
        _host.Start(endPoint);
    }

    public void Update()
    {
        _host.Poll();
    }

    private static void Print(string text)
    {
        Console.WriteLine("[SERVER]:" + text);
    }
}