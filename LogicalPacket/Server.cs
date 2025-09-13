using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LogicalPacket.Core;

namespace LogicalPacket;

public sealed class Server : IDisposable
{
    private const int MaxPacketSize = 1024;

    private readonly INetEventListener _eventListener;
    private readonly Socket _socket;
    private readonly MemoryPool<byte> _bufferPool;
    private readonly IPEndPoint _endPointFactory;

    private readonly ConcurrentDictionary<IPEndPoint, Peer> _peers;
    private readonly ConcurrentDictionary<SocketAddress, IPEndPoint> _endPointCache;
    private readonly ConcurrentDictionary<IPEndPoint, SocketAddress> _addressCache;
    private readonly ConcurrentQueue<NetEvent> _eventQueue;

    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public Server(INetEventListener eventListener)
    {
        _eventListener = eventListener;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _bufferPool = MemoryPool<byte>.Shared;
        _endPointFactory = new IPEndPoint(IPAddress.Any, 0);

        _peers = [];
        _endPointCache = [];
        _addressCache = [];
        _eventQueue = [];
    }

    public void Dispose()
    {
        Stop();
        _socket.Dispose();
    }

    public void Start(int port)
    {
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
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
                case ConnectEvent connectEvent:
                    _eventListener.OnPeerConnected(connectEvent.Peer);
                    break;
                case DisconnectEvent disconnectEvent:
                    _eventListener.OnPeerDisconnected(disconnectEvent.Peer);
                    break;
                case ReceiveEvent receiveEvent:
                    var reader = new PacketReader(receiveEvent.Packet.Payload.Span);
                    _eventListener.OnPacketReceived(receiveEvent.Peer, reader, receiveEvent.DeliveryMethod);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown netEvent: {nameof(netEvent)}");
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

        var connectAcceptPacket = new Packet(PacketType.ConnectAccept);
        Send(connectAcceptPacket, remoteEndPoint);

        EnqueueEvent(NetEvents.Connect(peer));
    }

    internal void RejectConnection(IPEndPoint remoteEndPoint)
    {
        var rejectPacket = new Packet(PacketType.ConnectReject);
        Send(rejectPacket, remoteEndPoint);
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
                Console.WriteLine(ex.Message);
            }
    }

    private void HandlePacket(Packet packet, IPEndPoint remoteEndPoint)
    {
        var peerFound = _peers.TryGetValue(remoteEndPoint, out var peer);

        switch (packet.Type)
        {
            case PacketType.ConnectRequest:
                ProcessConnectRequest(packet, remoteEndPoint, peer);
                packet.Dispose();
                break;
            case PacketType.Disconnect:
                break;
            default:
                if (peerFound)
                    peer!.ProcessPacket(packet);
                else
                    packet.Dispose();
                break;
        }
    }

    private void ProcessConnectRequest(Packet packet, IPEndPoint remoteEndPoint, Peer? peer)
    {
        if (peer != null) return;

        var request = new ConnectionRequest(this, remoteEndPoint, packet);

        _eventListener.OnConnectionRequest(request);
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