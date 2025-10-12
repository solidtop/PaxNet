using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

public class Host : IDisposable
{
    private readonly Transport _transport = new(1024);

    private readonly ConcurrentDictionary<IPEndPoint, Connection> _connections = [];
    private readonly Dictionary<SocketAddress, IPEndPoint> _endpointCache = [];
    private readonly ConcurrentQueue<NetEvent> _eventQueue = [];
    private readonly IPEndPoint _endPointFactory = new(IPAddress.Any, 0);

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _maintenanceTask;

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stop();
        _transport.Dispose();
    }

    public event Action<ConnectionRequest>? ConnectionRequested;
    public event Action<Connection>? ClientConnected;
    public event Action<Connection, DisconnectInfo>? ClientDisconnected;
    public event Action<Connection, TimeSpan>? RttUpdated;

    public void Start(IPEndPoint localEndPoint)
    {
        if (IsRunning)
            return;

        Console.WriteLine($"[HOST]: Starting on {localEndPoint}.");
        IsRunning = true;
        _transport.Bind(localEndPoint);
        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
        _maintenanceTask = Task.Run(() => MaintenanceLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        Console.WriteLine("[HOST]: Stopping..");
        IsRunning = false;
        _cts?.Cancel();
        _receiveTask?.Wait();
        _maintenanceTask?.Wait();
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _maintenanceTask = null;
    }

    public void Connect(IPEndPoint remoteEndPoint, string key)
    {
        Console.WriteLine($"[HOST]: Connecting to {remoteEndPoint}.");

        var localEndPoint = new IPEndPoint(IPAddress.Any, 0);
        Start(localEndPoint);

        var connection = EnsureConnection(remoteEndPoint);
        _connections.TryAdd(remoteEndPoint, connection);

        using var requestPacket = Packet.CreateConnectRequest(key);
        _transport.Send(requestPacket.Data, remoteEndPoint);
    }

    public void DisconnectAll()
    {
        foreach (var connection in _connections.Values) connection.Disconnect();
    }

    public void PollEvents()
    {
        while (_eventQueue.TryDequeue(out var netEvent))
            switch (netEvent)
            {
                case ConnectionRequestEvent requestEvent:
                    ConnectionRequested?.Invoke(requestEvent.Request);
                    break;
                case ConnectEvent connectEvent:
                    ClientConnected?.Invoke(connectEvent.Connection);
                    break;
                case DisconnectEvent disconnectEvent:
                    ClientDisconnected?.Invoke(disconnectEvent.Connection, disconnectEvent.Info);
                    break;
                case RttEvent rttEvent:
                    RttUpdated?.Invoke(rttEvent.Connection, rttEvent.Rtt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(netEvent));
            }
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var receivedAddress = new SocketAddress(_transport.AddressFamily);

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var packet = await _transport.ReceiveAsync(receivedAddress, cancellationToken);

                if (packet.Size == 0)
                {
                    packet.Dispose();
                    continue;
                }

                var remoteEndPoint = GetEndPoint(receivedAddress);
                var connection = EnsureConnection(remoteEndPoint);
                connection.HandlePacket(packet);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[HOST]: Error receiving packet: {ex}");
            }
    }

    private async Task MaintenanceLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var now = DateTime.UtcNow;

                foreach (var connection in _connections.Values)
                    connection.KeepAlive(now);

                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
    }

    private Connection EnsureConnection(IPEndPoint remoteEndPoint)
    {
        if (_connections.TryGetValue(remoteEndPoint, out var connection)) return connection;

        connection = new Connection(_transport, remoteEndPoint);
        connection.Requested += OnConnectionRequested;
        connection.Accepted += OnConnectionAccepted;
        connection.Rejected += OnConnectionRejected;
        connection.Disconnected += OnConnectionDisconnected;
        connection.RttUpdated += OnRttUpdated;
        _connections.TryAdd(remoteEndPoint, connection);

        return connection;
    }

    private IPEndPoint GetEndPoint(SocketAddress address)
    {
        if (_endpointCache.TryGetValue(address, out var endpoint)) return endpoint;

        endpoint = (IPEndPoint)_endPointFactory.Create(address);

        var addressCopy = new SocketAddress(address.Family, address.Size);
        address.Buffer.CopyTo(addressCopy.Buffer);
        _endpointCache.TryAdd(address, endpoint);

        return endpoint;
    }

    private void OnConnectionRequested(ConnectionRequest request)
    {
        _eventQueue.Enqueue(NetEvents.ConnectionRequest(request));
    }

    private void OnConnectionAccepted(Connection connection)
    {
        _eventQueue.Enqueue(NetEvents.Connect(connection));
    }

    private void OnConnectionRejected(Connection connection)
    {
        _connections.TryRemove(connection.RemoteEndPoint, out _);
    }

    private void OnConnectionDisconnected(Connection connection, DisconnectInfo info)
    {
        _connections.TryRemove(connection.RemoteEndPoint, out _);
        _eventQueue.Enqueue(NetEvents.Disconnect(connection, info));
    }

    private void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
        _eventQueue.Enqueue(NetEvents.Rtt(connection, rtt));
    }
}