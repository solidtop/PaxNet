using System.Net;
using PaxNet;

const string key = "MySecretKey";
var serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5555);

var server = new Server(serverEndPoint, key);
var client = new Client(serverEndPoint, key);

server.Start();
client.Start();

while (!Console.KeyAvailable)
{
    server.Update();
    client.Update();
}

server.Stop();
client.Stop();

internal class Server
{
    private readonly IPEndPoint _localEndPoint;
    private readonly string _key;
    private readonly Host _host;

    public Server(IPEndPoint localEndPoint, string key)
    {
        _localEndPoint = localEndPoint;
        _key = key;
        _host = new Host();
        _host.ConnectionRequested += OnConnectionRequested;
        _host.ClientConnected += OnClientConnected;
        _host.ClientDisconnected += OnClientDisconnected;
        _host.RttUpdated += OnRttUpdated;
    }

    public void Start()
    {
        _host.Start(_localEndPoint);
    }

    public void Stop()
    {
        _host.Stop();
    }

    public void Update()
    {
        _host.PollEvents();
    }

    private void OnConnectionRequested(ConnectionRequest request)
    {
        request.AcceptIfKey(_key);
    }

    private void OnClientConnected(Connection connection)
    {
        Print($"Client {connection.RemoteEndPoint} connected.");
    }

    private void OnClientDisconnected(Connection connection, DisconnectInfo info)
    {
        Print($"Client {connection.RemoteEndPoint} disconnected with reason: {info.Reason}.");
    }

    private void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
        Print($"[{connection.RemoteEndPoint}] Ping: {rtt.Milliseconds} ms");
    }

    private static void Print(string text)
    {
        Console.WriteLine($"[SERVER]: {text}");
    }
}

internal class Client
{
    private readonly IPEndPoint _remoteEndPoint;
    private readonly string _key;
    private readonly Host _host;

    public Client(IPEndPoint remoteEndPoint, string key)
    {
        _remoteEndPoint = remoteEndPoint;
        _key = key;
        _host = new Host();
        _host.ClientConnected += OnClientConnected;
        _host.ClientDisconnected += OnClientDisconnected;
        _host.RttUpdated += OnRttUpdated;
    }

    public void Start()
    {
        _host.Connect(_remoteEndPoint, _key);
    }

    public void Stop()
    {
        _host.Stop();
    }

    public void Update()
    {
        _host.PollEvents();
    }

    private void OnClientConnected(Connection connection)
    {
        Print("Connected.");
    }

    private void OnClientDisconnected(Connection connection, DisconnectInfo info)
    {
        Print($"Disconnected with reason: {info.Reason}.");
    }

    private void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
        Print($"Ping: {rtt.Milliseconds} ms");
    }

    private static void Print(string text)
    {
        Console.WriteLine($"[CLIENT]: {text}");
    }
}