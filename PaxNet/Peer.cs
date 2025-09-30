using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

namespace PaxNet;

public sealed class Peer : IPEndPoint
{
    private const double RttAlpha = 0.125;
    private const double DevAlpha = 0.25;

    private readonly Server _server;
    private readonly MemoryPool<byte> _bufferPool;
    private readonly UnreliableChannel _unreliableChannel;

    // Lifecycle
    private readonly TimeSpan _keepAliveInterval;
    private readonly TimeSpan _timeoutInterval;
    private TimeSpan _rtt;
    private TimeSpan _smoothedRtt;
    private TimeSpan _rttJitter;
    private DateTime _lastSend;
    private DateTime _lastReceive;

    private CancellationTokenSource? _cts;
    private Task? _sendTask;
    private Task? _lifecycleTask;

    internal Peer(Server server, IPEndPoint remoteEndPoint) : base(remoteEndPoint.Address, remoteEndPoint.Port)
    {
        _server = server;
        _bufferPool = MemoryPool<byte>.Shared;

        _unreliableChannel = new UnreliableChannel(server, this, 8);

        _keepAliveInterval = TimeSpan.FromSeconds(5);
        _timeoutInterval = TimeSpan.FromSeconds(15);
        _rtt = TimeSpan.Zero;
        _smoothedRtt = TimeSpan.Zero;
        _rttJitter = TimeSpan.Zero;
        _lastSend = DateTime.UtcNow;
        _lastReceive = DateTime.UtcNow;
    }

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public void Send(ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
    {
        var type = deliveryMethod switch
        {
            _ => PacketType.Unreliable
        };

        var headerSize = Packet.GetHeaderSize(type);
        var packetSize = headerSize + payload.Length;
        var bufferOwner = _bufferPool.Rent(packetSize);

        // Reserve header space, then copy payload
        payload.CopyTo(bufferOwner.Memory.Span[headerSize..]);

        var packet = new Packet(bufferOwner, packetSize)
        {
            Type = type
        };

        // Write directly to unreliable for now
        _unreliableChannel.TryWrite(packet);
        _lastSend = DateTime.UtcNow;
    }

    public void Disconnect()
    {
        _server.DisconnectPeer(this, DisconnectReason.LocalClose);
    }

    internal void Connect(CancellationToken cancellationToken)
    {
        ConnectionState = ConnectionState.Connected;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sendTask = Task.Run(() => _unreliableChannel.ProcessAsync(_cts.Token), _cts.Token);
        _lifecycleTask = Task.Run(() => LifecycleLoopAsync(_cts.Token), _cts.Token);
    }

    internal void Shutdown()
    {
        ConnectionState = ConnectionState.Disconnected;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _unreliableChannel.Complete();
    }

    internal void HandlePacket(Packet packet)
    {
        _lastReceive = DateTime.UtcNow;

        switch (packet.Type)
        {
            case PacketType.Ping:
                SendPongEcho(packet);
                break;
            case PacketType.Pong:
                UpdateRoundTripTime(packet);
                break;
            case PacketType.Unreliable:
                _server.EnqueueEvent(NetEvents.Receive(this, packet, DeliveryMethod.Unreliable));
                break;
        }

        packet.Dispose();
    }

    private async Task LifecycleLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // Send keep-alive
            if (now - _lastSend > _keepAliveInterval)
            {
                SendPing();
                _lastSend = now;
            }

            // Timeout detection
            if (now - _lastReceive > _timeoutInterval)
            {
                _server.DisconnectPeer(this, DisconnectReason.Timeout);
                return;
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private void SendPing()
    {
        var headerSize = Packet.GetHeaderSize(PacketType.Ping);
        const int payloadSize = 8; // int64 ticks from Stopwatch
        var packetSize = headerSize + payloadSize;

        var bufferOwner = _bufferPool.Rent(packetSize);
        var span = bufferOwner.Memory.Span;

        // Write timestamp payload
        var ticks = Stopwatch.GetTimestamp();
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(headerSize, payloadSize), ticks);

        var pingPacket = new Packet(bufferOwner, packetSize) { Type = PacketType.Ping };
        _server.Send(pingPacket, this);
    }

    private void SendPongEcho(Packet pingPacket)
    {
        var headerSize = Packet.GetHeaderSize(PacketType.Pong);
        var payloadSpan = pingPacket.Payload.Span;
        var packetSize = headerSize + payloadSpan.Length;

        var bufferOwner = _bufferPool.Rent(packetSize);
        var span = bufferOwner.Memory.Span;

        payloadSpan.CopyTo(span[headerSize..]);

        var pongPacket = new Packet(bufferOwner, packetSize) { Type = PacketType.Pong };
        _server.Send(pongPacket, this);
    }

    private void UpdateRoundTripTime(Packet pongPacket)
    {
        var sentTicks = BinaryPrimitives.ReadInt64LittleEndian(pongPacket.Payload.Span);
        var sample = Stopwatch.GetElapsedTime(sentTicks);
        _rtt = sample;

        if (_smoothedRtt == TimeSpan.Zero)
        {
            _smoothedRtt = sample;
            _rttJitter = TimeSpan.Zero;
        }
        else
        {
            var err = sample - _smoothedRtt;
            _smoothedRtt += TimeSpan.FromTicks((long)(err.Ticks * RttAlpha));
            _rttJitter += TimeSpan.FromTicks((long)((Math.Abs(err.Ticks) - _rttJitter.Ticks) * DevAlpha));
        }

        _server.EnqueueEvent(NetEvents.Rtt(this, _smoothedRtt));
    }
}

public enum ConnectionState
{
    Connecting,
    Connected,
    Disconnecting,
    Disconnected
}