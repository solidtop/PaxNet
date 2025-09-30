using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace PaxNet;

public sealed class Server : IDisposable
{
    private const int MaxPacketSize = 1024;

    private readonly Socket _socket;
    private readonly MemoryPool<byte> _bufferPool;
    private readonly IPEndPoint _endPointFactory;

    private readonly ConcurrentDictionary<IPEndPoint, Peer> _peers;
    private readonly ConcurrentDictionary<SocketAddress, IPEndPoint> _endPointCache;
    private readonly ConcurrentDictionary<IPEndPoint, SocketAddress> _addressCache;
    private readonly ConcurrentQueue<NetEvent> _eventQueue;

    private readonly Packet _connectAcceptPacket;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public Server()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _bufferPool = MemoryPool<byte>.Shared;
        _endPointFactory = new IPEndPoint(IPAddress.Any, 0);

        _peers = [];
        _endPointCache = [];
        _addressCache = [];
        _eventQueue = [];

        _connectAcceptPacket = new Packet(PacketType.ConnectAccept);
    }

    public int ConnectedPeers => _peers.Count;

    public void Dispose()
    {
        Stop();
        _socket.Dispose();
    }

    // Events
    public event Action<ConnectionRequest>? ConnectionRequested;
    public event Action<Peer>? PeerConnected;
    public event Action<Peer, DisconnectInfo>? PeerDisconnected;
    public event Action<Peer, PacketReader, DeliveryMethod>? PacketReceived;
    public event Action<Peer, TimeSpan>? RttUpdated;
    public event Action<IPEndPoint, SocketError>? ErrorOccured;

    public void Start(string address, int port)
    {
        _socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
        _cts = new CancellationTokenSource();

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        Console.WriteLine($"Server started on port {port}...");
    }

    public void Stop()
    {
        Console.WriteLine("Server shutting down...");

        _cts?.Cancel();
        _receiveTask?.Wait();
        _socket.Close();
        _peers.Clear();
        _endPointCache.Clear();
        _addressCache.Clear();
        _eventQueue.Clear();
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
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
                    PeerConnected?.Invoke(connectEvent.Peer);
                    break;
                case DisconnectEvent disconnectEvent:
                    PeerDisconnected?.Invoke(disconnectEvent.Peer, disconnectEvent.Info);
                    break;
                case ReceiveEvent receiveEvent:
                    var reader = new PacketReader(receiveEvent.Packet.Payload.Span);
                    PacketReceived?.Invoke(receiveEvent.Peer, reader, receiveEvent.DeliveryMethod);
                    break;
                case ErrorEvent errorEvent:
                    ErrorOccured?.Invoke(errorEvent.RemoteEndPoint, errorEvent.Error);
                    break;
                case RttEvent rttEvent:
                    RttUpdated?.Invoke(rttEvent.Peer, rttEvent.Rtt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown Event: {nameof(netEvent)}");
            }
    }

    internal int Send(Packet packet, IPEndPoint remoteEndPoint)
    {
        try
        {
            var address = _addressCache.GetOrAdd(remoteEndPoint, ep => ep.Serialize());
            return _socket.SendTo(packet.Data.Span, SocketFlags.None, address);
        }
        catch (SocketException ex)
        {
            Console.WriteLine(ex.Message);
            return 0;
        }
        finally
        {
            packet.Dispose();
        }
    }

    internal ValueTask<int> SendAsync(Packet packet, IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = _addressCache.GetOrAdd(remoteEndPoint, ep => ep.Serialize());
            return _socket.SendToAsync(packet.Data, SocketFlags.None, address, cancellationToken);
        }
        catch (SocketException ex)
        {
            Console.WriteLine(ex.Message);
            return ValueTask.FromResult(0);
        }
        finally
        {
            packet.Dispose();
        }
    }

    internal void EnqueueEvent(NetEvent netEvent)
    {
        _eventQueue.Enqueue(netEvent);
    }

    internal void AcceptConnection(IPEndPoint remoteEndPoint)
    {
        var peer = new Peer(this, remoteEndPoint);
        if (!_peers.TryAdd(remoteEndPoint, peer)) return;

        peer.Connect(_cts!.Token);

        Send(_connectAcceptPacket, remoteEndPoint);

        EnqueueEvent(NetEvents.Connect(peer));
    }

    internal void RejectConnection(IPEndPoint remoteEndPoint)
    {
        var rejectPacket = new Packet(PacketType.ConnectReject);
        Send(rejectPacket, remoteEndPoint);
    }

    internal void DisconnectPeer(Peer peer, DisconnectReason reason, SocketError error = SocketError.Success)
    {
        DisconnectPeer(peer, new DisconnectInfo(reason, error));
    }

    private void DisconnectPeer(Peer peer, DisconnectInfo info)
    {
        _peers.TryRemove(peer, out _);

        peer.Shutdown();

        EnqueueEvent(NetEvents.Disconnect(peer, info));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        // Preallocate SocketAddress for reuse
        var receivedAddress = new SocketAddress(_socket.AddressFamily);

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var bufferOwner = _bufferPool.Rent(MaxPacketSize);
                var buffer = bufferOwner.Memory;

                var bytesReceived =
                    await _socket.ReceiveFromAsync(buffer, SocketFlags.None, receivedAddress, cancellationToken);

                if (bytesReceived == 0)
                {
                    bufferOwner.Dispose();
                    continue;
                }

                var remoteEndPoint = GetEndPoint(receivedAddress);
                var packet = new Packet(bufferOwner, bytesReceived);
                HandlePacket(packet, remoteEndPoint);
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
                EnqueueEvent(NetEvents.Error(GetEndPoint(receivedAddress), ex.SocketErrorCode));
                Console.WriteLine(ex.Message);
            }
    }

    private void HandlePacket(Packet packet, IPEndPoint remoteEndPoint)
    {
        _peers.TryGetValue(remoteEndPoint, out var peer);

        switch (packet.Type)
        {
            case PacketType.ConnectRequest:
                HandleConnectRequest(packet, remoteEndPoint, peer);
                packet.Dispose();
                break;
            case PacketType.Disconnect:
                HandleDisconnect(peer);
                packet.Dispose();
                break;
            default:
                if (peer != null)
                    peer.HandlePacket(packet);
                else
                    packet.Dispose();
                break;
        }
    }

    private void HandleConnectRequest(Packet packet, IPEndPoint remoteEndPoint, Peer? peer)
    {
        if (peer is { ConnectionState: ConnectionState.Connected })
        {
            Send(_connectAcceptPacket, remoteEndPoint);
            return;
        }

        ;

        try
        {
            var reader = new PacketReader(packet.Payload.Span);
            var requestPacket = ConnectionRequestPacket.Read(reader);
            requestPacket.Payload = packet.Payload[ConnectionRequestPacket.HeaderSize..];

            var request = new ConnectionRequest(this, remoteEndPoint, requestPacket);
            EnqueueEvent(NetEvents.ConnectionRequest(request));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private void HandleDisconnect(Peer? peer)
    {
        if (peer == null) return;

        DisconnectPeer(peer, DisconnectReason.RemoteClose);

        var shutdownPacket = new Packet(PacketType.Shutdown);
        Send(shutdownPacket, peer);
    }

    private IPEndPoint GetEndPoint(SocketAddress address)
    {
        if (_endPointCache.TryGetValue(address, out var endPoint)) return endPoint;

        endPoint = (IPEndPoint)_endPointFactory.Create(address);

        var addressCopy = new SocketAddress(address.Family, address.Size);
        address.Buffer.CopyTo(addressCopy.Buffer);
        _endPointCache.TryAdd(addressCopy, endPoint);

        return endPoint;
    }
}