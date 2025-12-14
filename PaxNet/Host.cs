using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

public class Host(IConnectionListener listener) : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly ConcurrentDictionary<IPEndPoint, Connection> _connections = [];
    private readonly ConcurrentQueue<Event> _eventQueue = [];
    private readonly Dictionary<SocketAddress, IPEndPoint> _endPointCache = [];
    private readonly IPEndPoint _endPointFactory = new(IPAddress.Any, 0);
    private readonly Stopwatch _stopwatch = new();

    private Thread? _receiveThread;
    private Thread? _updateThread;

    public bool IsRunning { get; private set; }

    public int MaxPacketSize { get; set; } = 1500;
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMilliseconds(15);
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan TimeoutInterval { get; set; } = TimeSpan.FromSeconds(15);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stop();
        _socket.Dispose();
    }

    public void Start(IPEndPoint localEndPoint)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        _socket.Bind(localEndPoint);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        _receiveThread = new Thread(ReceiveLoop)
        {
            Name = "ReceiveLoop",
            IsBackground = true
        };

        _updateThread = new Thread(UpdateLoop)
        {
            Name = "UpdateLoop",
            IsBackground = true
        };

        _stopwatch.Start();
        _receiveThread.Start();
        _updateThread.Start();
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _stopwatch.Stop();
        _receiveThread?.Join();
        _updateThread?.Join();
        _receiveThread = null;
        _updateThread = null;
    }

    public Connection Connect(IPEndPoint remoteEndPoint, string key)
    {
        var localEndPoint = new IPEndPoint(IPAddress.Any, 0);
        Start(localEndPoint);

        using var requestPacket = Packet.CreateConnectionRequest(key);
        SendTo(requestPacket.Data, remoteEndPoint);

        return EnsureConnection(remoteEndPoint);
    }

    public void Disconnect()
    {
        foreach (var connection in _connections.Values)
            connection.Disconnect();
    }

    public int SendTo(ReadOnlySpan<byte> data, IPEndPoint remoteEndPoint)
    {
        return _socket.SendTo(data, remoteEndPoint);
    }

    public void SendToAll(ReadOnlySpan<byte> data, DeliveryMethod deliveryMethod)
    {
        foreach (var connection in _connections.Values)
            connection.Send(data, deliveryMethod);
    }

    public void Poll()
    {
        while (_eventQueue.TryDequeue(out var @event))
            switch (@event)
            {
                case ConnectionRequestEvent requestEvent:
                    listener.OnConnectionRequested(requestEvent.Request);
                    break;
                case ConnectEvent connectEvent:
                    listener.OnConnected(connectEvent.Connection);
                    break;
                case DisconnectEvent disconnectEvent:
                    listener.OnDisconnected(disconnectEvent.Connection, disconnectEvent.Reason);
                    break;
                case ReceiveEvent receiveEvent:
                    listener.OnDataReceived(receiveEvent.Connection, receiveEvent.Packet.Reader,
                        receiveEvent.DeliveryMethod);
                    receiveEvent.Packet.Dispose();
                    break;
                case RttEvent rttEvent:
                    listener.OnRttUpdated(rttEvent.Connection, rttEvent.Rtt);
                    break;
                case ErrorEvent errorEvent:
                    listener.OnErrorOccured(errorEvent.RemoteEndPoint, errorEvent.Error);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@event));
            }
    }

    internal void OnConnectionRequested(ConnectionRequest request)
    {
        _eventQueue.Enqueue(Events.ConnectionRequest(request));
    }

    internal void OnConnectionAccepted(Connection connection)
    {
        _eventQueue.Enqueue(Events.Connect(connection));
    }

    internal void OnConnectionRejected(Connection connection)
    {
        _connections.TryRemove(connection.RemoteEndPoint, out _);
        _eventQueue.Enqueue(Events.Disconnect(connection, DisconnectReason.Reject));
    }

    internal void OnConnectionClosed(Connection connection, DisconnectReason reason)
    {
        _connections.TryRemove(connection.RemoteEndPoint, out _);
        _eventQueue.Enqueue(Events.Disconnect(connection, reason));
    }

    internal void OnDataReceived(Connection connection, Packet packet, DeliveryMethod deliveryMethod)
    {
        _eventQueue.Enqueue(Events.Receive(connection, packet, deliveryMethod));
    }

    internal void OnRttUpdated(Connection connection, TimeSpan rtt)
    {
        _eventQueue.Enqueue(Events.Rtt(connection, rtt));
    }

    internal void OnErrorOccured(Connection connection, SocketError error)
    {
        _eventQueue.Enqueue(Events.Error(connection.RemoteEndPoint, error));
    }

    private Packet ReceivePacketFrom(SocketAddress address)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);
        var bytesReceived = _socket.ReceiveFrom(buffer, SocketFlags.None, address);
        return new Packet(buffer, bytesReceived);
    }

    private void ReceiveLoop()
    {
        var address = new SocketAddress(_socket.AddressFamily);

        while (IsRunning)
            try
            {
                var packet = ReceivePacketFrom(address);

                if (packet.IsEmpty)
                {
                    packet.Dispose();
                    continue;
                }

                var remoteEndPoint = GetEndPoint(address);
                var connection = EnsureConnection(remoteEndPoint);
                connection.HandlePacket(packet, _stopwatch.Elapsed);
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                var remoteEndPoint = GetEndPoint(address);
                listener.OnErrorOccured(remoteEndPoint, ex.SocketErrorCode);
            }
    }

    private void UpdateLoop()
    {
        var nextTick = _stopwatch.Elapsed;

        while (IsRunning)
            try
            {
                var elapsed = _stopwatch.Elapsed;

                if (elapsed >= nextTick)
                {
                    foreach (var connection in _connections.Values)
                        connection.Update(elapsed);

                    nextTick += UpdateInterval;

                    if (elapsed > nextTick)
                        nextTick = elapsed;
                }
                else
                {
                    var msTimeout = (int)(nextTick - elapsed).TotalMilliseconds;
                    if (msTimeout > 0) Thread.Sleep(msTimeout);
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
    }

    private IPEndPoint GetEndPoint(SocketAddress address)
    {
        if (_endPointCache.TryGetValue(address, out var endPoint))
            return endPoint;

        endPoint = (IPEndPoint)_endPointFactory.Create(address);

        var addressCopy = new SocketAddress(address.Family, address.Size);
        address.Buffer.CopyTo(addressCopy.Buffer);
        _endPointCache.TryAdd(address, endPoint);

        return endPoint;
    }

    private Connection EnsureConnection(IPEndPoint remoteEndPoint)
    {
        if (_connections.TryGetValue(remoteEndPoint, out var connection))
            return connection;

        connection = new Connection(this, remoteEndPoint)
        {
            KeepAliveInterval = KeepAliveInterval,
            TimeoutInterval = TimeoutInterval
        };

        _connections[remoteEndPoint] = connection;
        return connection;
    }
}