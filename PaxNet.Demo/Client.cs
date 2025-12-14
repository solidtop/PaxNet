using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace PaxNet.Demo;

public class Client : IConnectionListener
{
    private static int _counter;
    private readonly int _id = ++_counter;
    private readonly Host _host;

    public Client()
    {
        _host = new Host(this);
    }

    public void OnConnectionRequested(ConnectionRequest request)
    {
    }

    public void OnConnected(Connection connection)
    {
        Print("Connected");
    }

    public void OnDisconnected(Connection connection, DisconnectReason reason)
    {
        Print($"Disconnected with reason: {reason}");
    }

    public void OnDataReceived(Connection connection, PacketReader reader, DeliveryMethod deliveryMethod)
    {
        Print(reader.ReadFloat().ToString(CultureInfo.InvariantCulture));
    }

    public void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
    }

    public void OnErrorOccured(IPEndPoint remoteEndPoint, SocketError error)
    {
        Print($"Error: {error}");
    }

    public void Connect(IPEndPoint endPoint)
    {
        _host.Connect(endPoint, "MyKey");
    }

    public void Update()
    {
        _host.Poll();
    }

    private void Print(string text)
    {
        Console.WriteLine($"[CLIENT {_id}]:" + text);
    }
}