using System.Buffers;
using System.Net;

namespace LogicalPacket.Core;

public sealed class Peer : IPEndPoint
{
    private readonly Server _server;
    private readonly MemoryPool<byte> _bufferPool;

    private readonly UnreliableChannel _unreliableChannel;

    private readonly Packet _pingPacket;
    private readonly Packet _pongPacket;

    private CancellationTokenSource? _cts;

    private Task? _sendTask;

    public Peer(Server server, IPEndPoint remoteEndPoint) : base(remoteEndPoint.Address, remoteEndPoint.Port)
    {
        _server = server;
        _bufferPool = MemoryPool<byte>.Shared;

        _unreliableChannel = new UnreliableChannel(server, this, 8);

        _pingPacket = new Packet(PacketType.Ping);
        _pongPacket = new Packet(PacketType.Pong);
    }

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
    }

    internal void Connect(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sendTask = Task.Run(() => _unreliableChannel.ProcessAsync(_cts.Token), _cts.Token);
    }

    internal async Task DisconnectAsync()
    {
        if (_cts != null) await _cts.CancelAsync();
        _unreliableChannel.Complete();
        if (_sendTask != null) await _sendTask;
        _cts?.Dispose();
    }

    internal void ProcessPacket(Packet packet)
    {
        switch (packet.Type)
        {
            case PacketType.Unreliable:
                _server.EnqueueEvent(NetEvents.Receive(this, packet, DeliveryMethod.Unreliable));
                packet.Dispose();
                break;
            case PacketType.Ping:
                _server.Send(_pongPacket, this);
                packet.Dispose();
                break;
        }
    }
}